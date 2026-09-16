using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Cli;

/// <summary>Executes typed CLI commands against the Flow domain services.</summary>
public sealed class CliOperations
{
    private readonly IFlowDocumentSerializer _serializer;
    private readonly IEpubImporter _epubImporter;
    private readonly IEpubPublicationInspector _epubInspector;
    private readonly DocumentValidator _validator;
    private readonly IDocumentIntegrityService _integrityService;
    private readonly ILayoutEngine _layoutEngine;
    private readonly IDocumentRenderer _htmlRenderer;
    private readonly IEpubFidelityAnalyzer _epubFidelityAnalyzer;
    private readonly IHtmlBookPackageRenderer _htmlBookRenderer;

    public CliOperations(
        IFlowDocumentSerializer serializer,
        IEpubImporter epubImporter,
        IEpubPublicationInspector epubInspector,
        DocumentValidator validator,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IDocumentRenderer htmlRenderer,
        IEpubFidelityAnalyzer? epubFidelityAnalyzer = null,
        IHtmlBookPackageRenderer? htmlBookRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(epubImporter);
        ArgumentNullException.ThrowIfNull(epubInspector);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);

        _serializer = serializer;
        _epubImporter = epubImporter;
        _epubInspector = epubInspector;
        _validator = validator;
        _integrityService = integrityService;
        _layoutEngine = layoutEngine;
        _htmlRenderer = htmlRenderer;
        _epubFidelityAnalyzer = epubFidelityAnalyzer ?? new EpubFidelityAnalyzer();
        _htmlBookRenderer = htmlBookRenderer ?? new HtmlBookPackageRenderer();
    }

    /// <summary>Executes a parsed command and writes its normal output.</summary>
    public async Task<int> ExecuteAsync(
        CliCommand command,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return command switch
        {
            HelpCommand => await ShowHelpAsync(output).ConfigureAwait(false),
            SampleCommand sample => await CreateSampleAsync(sample, output, cancellationToken).ConfigureAwait(false),
            ImportEpubCommand import => await ImportEpubAsync(import, output, error, cancellationToken)
                .ConfigureAwait(false),
            InspectEpubCommand inspectEpub => await InspectEpubAsync(inspectEpub, output, error, cancellationToken)
                .ConfigureAwait(false),
            InspectCommand inspect => await InspectAsync(inspect, output, cancellationToken).ConfigureAwait(false),
            ValidateCommand validate => await ValidateAsync(validate, output, cancellationToken).ConfigureAwait(false),
            HashCommand hash => await HashAsync(hash, output, cancellationToken).ConfigureAwait(false),
            RenderHtmlCommand render => await RenderHtmlAsync(render, output, cancellationToken).ConfigureAwait(false),
            RenderHtmlBookCommand renderBook => await RenderHtmlBookAsync(renderBook, output, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown CLI command."),
        };
    }

    private static async Task<int> ShowHelpAsync(TextWriter output)
    {
        await output.WriteLineAsync("Flow Engine .NET 0.2.0-alpha.1 (experimental)").ConfigureAwait(false);
        await output.WriteLineAsync("The .flow.json format and all 0.x APIs may change.").ConfigureAwait(false);
        await output.WriteLineAsync("Commands:").ConfigureAwait(false);
        await output.WriteLineAsync("  flow sample [output]                         Create the reference .flow.json book.")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "  flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>]")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "    Without --output, the file name is derived from the imported book title.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow epub-inspect <book.epub> [--json <report.json>]")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow inspect <document>                      Show semantic document counts.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow validate <document>                     Validate semantic invariants.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow hash <document>                         Compute the canonical SHA-256 hash.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow render <document> --html <output> --width <n> --height <n>")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "  flow render <document> --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>]")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Exit codes: 0 success, 1 command/input failure, 2 semantic validation failure.")
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> InspectEpubAsync(
        InspectEpubCommand command,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync("FLOWCLI_UNSUPPORTED_INPUT: epub-inspect accepts only .epub files.")
                .ConfigureAwait(false);
            return 1;
        }

        var jsonOutputPath = command.JsonOutputPath is null ? null : Path.GetFullPath(command.JsonOutputPath);
        if (jsonOutputPath is not null && PathsEqual(sourcePath, jsonOutputPath))
        {
            await error.WriteLineAsync("FLOWCLI_INVALID_OUTPUT: The JSON report path must differ from the EPUB source path.")
                .ConfigureAwait(false);
            return 1;
        }

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await _epubInspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
        await WriteEpubInspectionAsync(inspection, sourcePath, output).ConfigureAwait(false);
        await WriteEpubDiagnosticsAsync(
                inspection.Diagnostics,
                output,
                error,
                detailsPersisted: command.JsonOutputPath is not null)
            .ConfigureAwait(false);

        if (jsonOutputPath is not null)
        {
            EnsureParentDirectory(jsonOutputPath);
            await WriteInspectionAtomicallyAsync(inspection, jsonOutputPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync($"JSON report: {jsonOutputPath}").ConfigureAwait(false);
        }

        return inspection.IsSuccess ? 0 : 1;
    }

    private static async Task WriteEpubInspectionAsync(
        EpubPublicationInspection inspection,
        string sourcePath,
        TextWriter output)
    {
        var package = inspection.Package;
        await output.WriteLineAsync($"EPUB inspection: {sourcePath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Status: {(inspection.IsSuccess ? "valid" : "invalid")}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Container: {inspection.ContainerPath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Package: {package?.Path ?? "(unavailable)"}").ConfigureAwait(false);
        await output.WriteLineAsync(
                $"EPUB version: {package?.VersionFamily.ToString() ?? "Unknown"} ({package?.DeclaredVersion ?? "unknown"})")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Title: {package?.Title ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Identifier: {package?.Identifier ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Language: {package?.Language ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Creators: {string.Join(", ", package?.Creators ?? [])}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Manifest items: {inspection.Resources.ManifestItemCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Spine items: {inspection.Spine.Length.ToString(CultureInfo.InvariantCulture)} "
                + $"(linear {inspection.Spine.Count(static item => item.IsLinear).ToString(CultureInfo.InvariantCulture)}, "
                + $"non-linear {inspection.Spine.Count(static item => !item.IsLinear).ToString(CultureInfo.InvariantCulture)})")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Navigation documents: {inspection.NavigationDocumentPaths.Length.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Archive entries: {inspection.Resources.ArchiveEntryCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Compressed bytes: {inspection.Resources.TotalCompressedBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Uncompressed bytes: {inspection.Resources.TotalUncompressedBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Resource types:").ConfigureAwait(false);
        foreach (var (mediaType, count) in inspection.Resources.MediaTypeCounts)
        {
            await output.WriteLineAsync($"  {mediaType}: {count.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);
        }
    }

    private async Task<int> ImportEpubAsync(
        ImportEpubCommand command,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        var outputPath = command.OutputPath is null ? null : Path.GetFullPath(command.OutputPath);
        var diagnosticsPath = command.DiagnosticsJsonOutputPath is null
            ? null
            : Path.GetFullPath(command.DiagnosticsJsonOutputPath);
        var fidelityPath = command.FidelityReportOutputPath is null
            ? null
            : Path.GetFullPath(command.FidelityReportOutputPath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync("FLOWCLI_UNSUPPORTED_INPUT: The import command currently accepts only .epub files.")
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is not null && PathsEqual(sourcePath, outputPath))
        {
            await error.WriteLineAsync("FLOWCLI_INVALID_OUTPUT: The output path must differ from the EPUB source path.")
                .ConfigureAwait(false);
            return 1;
        }

        if (diagnosticsPath is not null && PathsEqual(sourcePath, diagnosticsPath))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The diagnostics report path must differ from the EPUB source path.")
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null && PathsEqual(sourcePath, fidelityPath))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The fidelity report path must differ from the EPUB source path.")
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is not null && diagnosticsPath is not null && PathsEqual(outputPath, diagnosticsPath))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The Flow document and diagnostics report must use different paths.")
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null
            && ((outputPath is not null && PathsEqual(outputPath, fidelityPath))
                || (diagnosticsPath is not null && PathsEqual(diagnosticsPath, fidelityPath))))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The fidelity report must use a path different from every other output.")
                .ConfigureAwait(false);
            return 1;
        }

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var import = await _epubImporter.ImportAsync(source, progress: null, cancellationToken).ConfigureAwait(false);
        var pipelineMetrics = import.Metrics;

        if (import.Document is not null && outputPath is null)
        {
            outputPath = Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory(),
                PortableBookFileName.FromTitle(import.Document.Metadata.Title, sourcePath));
        }

        if (outputPath is not null && diagnosticsPath is not null && PathsEqual(outputPath, diagnosticsPath))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The Flow document and diagnostics report must use different paths.")
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null
            && ((outputPath is not null && PathsEqual(outputPath, fidelityPath))
                || (diagnosticsPath is not null && PathsEqual(diagnosticsPath, fidelityPath))))
        {
            await error.WriteLineAsync(
                    "FLOWCLI_INVALID_OUTPUT: The fidelity report must use a path different from every other output.")
                .ConfigureAwait(false);
            return 1;
        }

        if (diagnosticsPath is not null)
        {
            EnsureParentDirectory(diagnosticsPath);
            await WriteImportDiagnosticsAtomicallyAsync(import, diagnosticsPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync($"Diagnostics JSON: {diagnosticsPath}").ConfigureAwait(false);
        }

        if (fidelityPath is not null)
        {
            var fidelityStarted = Stopwatch.GetTimestamp();
            var fidelity = _epubFidelityAnalyzer.Analyze(import, cancellationToken);
            pipelineMetrics = pipelineMetrics?.AddPhaseTiming(
                EpubImportPhase.AnalyzingFidelity,
                Stopwatch.GetElapsedTime(fidelityStarted));
            EnsureParentDirectory(fidelityPath);
            await WriteFidelityReportAtomicallyAsync(fidelity, fidelityPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync($"Fidelity report: {fidelityPath}").ConfigureAwait(false);
        }

        await WriteEpubDiagnosticsAsync(
                import.Diagnostics,
                output,
                error,
                detailsPersisted: diagnosticsPath is not null)
            .ConfigureAwait(false);

        if (!import.IsSuccess || import.Document is null)
        {
            await WriteImportMetricsAsync(pipelineMetrics, output).ConfigureAwait(false);
            await error.WriteLineAsync("FLOWCLI_EPUB_IMPORT_FAILED: No complete Flow document was written.")
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is null)
        {
            throw new InvalidOperationException("A successful EPUB import did not produce an output path.");
        }

        var validation = _validator.Validate(import.Document);
        if (!validation.IsValid)
        {
            foreach (var diagnostic in validation.Diagnostics)
            {
                var location = diagnostic.NodeId is null ? string.Empty : $" [{diagnostic.NodeId}]";
                await error.WriteLineAsync(
                        $"{diagnostic.Severity} {diagnostic.Code}{location}: {diagnostic.Message}")
                    .ConfigureAwait(false);
            }

            await error.WriteLineAsync("FLOWCLI_EPUB_DOCUMENT_INVALID: The imported document was not written.")
                .ConfigureAwait(false);
            return 2;
        }

        EnsureParentDirectory(outputPath);
        var serializationStarted = Stopwatch.GetTimestamp();
        await WriteDocumentAtomicallyAsync(import.Document, outputPath, cancellationToken).ConfigureAwait(false);
        pipelineMetrics = pipelineMetrics?
            .AddPhaseTiming(EpubImportPhase.SerializingDocument, Stopwatch.GetElapsedTime(serializationStarted))
            .WithOutputSizes(new FileInfo(outputPath).Length, null, null);

        var hash = _integrityService.ComputeHash(import.Document);
        await output.WriteLineAsync($"Imported EPUB: {sourcePath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Flow document: {outputPath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Title: {import.Document.Metadata.Title}").ConfigureAwait(false);
        await output.WriteLineAsync($"Document ID: {import.Document.Identity.Id}").ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Nodes: {import.Document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Assets: {import.Document.Assets.Count.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await WriteHashAsync(output, hash).ConfigureAwait(false);
        await WriteImportMetricsAsync(pipelineMetrics, output).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> CreateSampleAsync(
        SampleCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(command.OutputPath);
        EnsureParentDirectory(path);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await _serializer.SerializeAsync(SampleBookFactory.Create(), stream, cancellationToken).ConfigureAwait(false);
        }

        await output.WriteLineAsync($"Sample written: {path}").ConfigureAwait(false);
        return 0;
    }

    private async Task<int> InspectAsync(
        InspectCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"ID: {document.Identity.Id}").ConfigureAwait(false);
        await output.WriteLineAsync($"Version: {document.Identity.Version ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Title: {document.Metadata.Title}").ConfigureAwait(false);
        await output.WriteLineAsync($"Subtitle: {document.Metadata.Subtitle ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Language: {document.Metadata.Language ?? "(none)"}").ConfigureAwait(false);
        await output.WriteLineAsync($"Authors: {string.Join(", ", document.Metadata.Authors)}").ConfigureAwait(false);
        await output.WriteLineAsync($"Nodes: {document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Chapters: {document.Content.Children.Count(static node => node is Chapter).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Sections: {CountNodes<Section>(document).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Paragraphs: {CountNodes<Paragraph>(document).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Figures: {CountNodes<Figure>(document).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Footnotes: {CountNodes<Footnote>(document).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Assets: {document.Assets.Count.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Anchors: {document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Presentation: {(document.Presentation is null ? "no" : "yes")}")
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ValidateAsync(
        ValidateCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var validation = _validator.Validate(document);
        if (validation.IsValid)
        {
            await output.WriteLineAsync("Valid: no semantic validation errors.").ConfigureAwait(false);
            return 0;
        }

        foreach (var diagnostic in validation.Diagnostics)
        {
            var location = diagnostic.NodeId is null ? string.Empty : $" [{diagnostic.NodeId}]";
            await output.WriteLineAsync($"{diagnostic.Severity} {diagnostic.Code}{location}: {diagnostic.Message}")
                .ConfigureAwait(false);
        }

        return 2;
    }

    private async Task<int> HashAsync(
        HashCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var hash = _integrityService.ComputeHash(document);
        await WriteHashAsync(output, hash).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RenderHtmlAsync(
        RenderHtmlCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var preferences = new UserReadingPreferences();
        var context = new LayoutContext(
            command.ViewportWidth,
            command.ViewportHeight,
            GetDeviceClass(command.ViewportWidth),
            ReadingMode.Flow,
            userPreferences: preferences);
        var layout = _layoutEngine.Layout(document, context);
        var rendered = _htmlRenderer.Render(document, layout, preferences);
        var outputPath = Path.GetFullPath(command.OutputPath);
        EnsureParentDirectory(outputPath);
        rendered.WriteTo(outputPath);

        var hash = _integrityService.ComputeHash(document);
        await output.WriteLineAsync($"Rendered: {outputPath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Document ID: {document.Identity.Id}").ConfigureAwait(false);
        await WriteHashAsync(output, hash).ConfigureAwait(false);
        await output.WriteLineAsync($"Anchors: {document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Viewport: {CssNumber(command.ViewportWidth)}x{CssNumber(command.ViewportHeight)} ({layout.Profile.ViewportCategory})")
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RenderHtmlBookAsync(
        RenderHtmlBookCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        const double width = 1024;
        const double height = 768;
        var documentPath = Path.GetFullPath(command.DocumentPath);
        var outputDirectory = Path.GetFullPath(command.OutputDirectory);
        ValidateHtmlBookOutputPath(documentPath, outputDirectory);

        var document = await ReadDocumentAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var preferences = new UserReadingPreferences();
        var layout = _layoutEngine.Layout(
            document,
            new LayoutContext(width, height, GetDeviceClass(width), ReadingMode.Flow, userPreferences: preferences));
        var hash = _integrityService.ComputeHash(document);
        var renderingStarted = Stopwatch.GetTimestamp();
        var package = _htmlBookRenderer.Render(
            document,
            layout,
            preferences,
            new HtmlBookIntegrity(hash.Algorithm, hash.Hash, hash.CanonicalizationVersion),
            new HtmlBookPackageOptions(command.UiLanguage),
            cancellationToken);
        var renderingDuration = Stopwatch.GetElapsedTime(renderingStarted);

        var writingStarted = Stopwatch.GetTimestamp();
        await WriteHtmlBookPackageAtomicallyAsync(package, outputDirectory, cancellationToken).ConfigureAwait(false);
        var writingDuration = Stopwatch.GetElapsedTime(writingStarted);
        await output.WriteLineAsync($"HTML book: {outputDirectory}").ConfigureAwait(false);
        await output.WriteLineAsync($"Entry: {Path.Combine(outputDirectory, "index.html")}").ConfigureAwait(false);
        await output.WriteLineAsync($"UI language: {UiLanguageName(command.UiLanguage, document.Metadata.Language)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Document ID: {document.Identity.Id}").ConfigureAwait(false);
        await WriteHashAsync(output, hash).ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Files: {package.Files.Length.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Chapters: {package.Files.Count(static file => file.Path.StartsWith("chapters/", StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"HTML bytes: {package.Files.Sum(static file => (long)file.Content.Length).ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Render duration ms: {Milliseconds(renderingDuration)}").ConfigureAwait(false);
        await output.WriteLineAsync($"Write duration ms: {Milliseconds(writingDuration)}").ConfigureAwait(false);
        return 0;
    }

    private static async Task WriteImportMetricsAsync(EpubImportMetrics? metrics, TextWriter output)
    {
        if (metrics is null)
        {
            return;
        }

        await output.WriteLineAsync("Import metrics (noncanonical):").ConfigureAwait(false);
        await output.WriteLineAsync($"  Total duration ms: {Milliseconds(metrics.TotalDuration)}").ConfigureAwait(false);
        await output.WriteLineAsync($"  Archive entries: {metrics.ArchiveEntryCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Compressed bytes: {metrics.CompressedBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Uncompressed bytes: {metrics.UncompressedBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Asset bytes: {metrics.AssetBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Spine documents: {metrics.SpineDocumentsProcessed.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Nodes: {metrics.NodesProduced.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Characters: {metrics.CharactersProduced.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"  Approximate peak managed bytes: {metrics.ApproximatePeakManagedBytes.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        if (metrics.FlowJsonBytes is not null)
        {
            await output.WriteLineAsync($"  Flow JSON bytes: {metrics.FlowJsonBytes.Value.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);
        }

        foreach (var timing in metrics.PhaseTimings)
        {
            await output.WriteLineAsync($"  Phase {timing.Phase}: {Milliseconds(timing.Duration)} ms")
                .ConfigureAwait(false);
        }
    }

    private static string Milliseconds(TimeSpan value) => value.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UiLanguageName(HtmlBookUiLanguage language, string? publicationLanguage)
    {
        if (language != HtmlBookUiLanguage.Automatic)
        {
            return language switch
            {
                HtmlBookUiLanguage.English => "en",
                HtmlBookUiLanguage.PortuguesePortugal => "pt-PT",
                HtmlBookUiLanguage.PortugueseBrazil => "pt-BR",
                _ => throw new ArgumentOutOfRangeException(nameof(language)),
            };
        }

        var normalized = publicationLanguage?.Replace('_', '-');
        if (normalized is not null
            && (normalized.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("pt-BR-", StringComparison.OrdinalIgnoreCase)))
        {
            return "pt-BR (automatic)";
        }

        return normalized is not null
               && (normalized.Equals("pt", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("pt-", StringComparison.OrdinalIgnoreCase))
            ? "pt-PT (automatic)"
            : "en (automatic fallback)";
    }

    private async Task<FlowDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDocumentAtomicallyAsync(
        FlowDocument document,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await _serializer.SerializeAsync(document, destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteInspectionAtomicallyAsync(
        EpubPublicationInspection inspection,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubInspectionJsonWriter.WriteAsync(inspection, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteImportDiagnosticsAtomicallyAsync(
        EpubImportResult import,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubImportDiagnosticsJsonWriter.WriteAsync(import, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteFidelityReportAtomicallyAsync(
        EpubFidelityReport report,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubFidelityJsonWriter.WriteAsync(report, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteHtmlBookPackageAtomicallyAsync(
        HtmlBookPackage package,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(outputDirectory)
            ?? throw new ArgumentException("The HTML book output must have a parent directory.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);
        if (Directory.Exists(outputDirectory))
        {
            ValidateReplaceableHtmlBookDirectory(outputDirectory);
        }

        var name = Path.GetFileName(outputDirectory);
        var temporaryDirectory = Path.Combine(parent, $".{name}.flow-html-book-{Guid.NewGuid():N}.tmp");
        var backupDirectory = Path.Combine(parent, $".{name}.flow-html-book-{Guid.NewGuid():N}.backup");
        Directory.CreateDirectory(temporaryDirectory);
        var destinationReplaced = false;
        try
        {
            foreach (var file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolvePackageOutputPath(temporaryDirectory, file.Path);
                var targetParent = Path.GetDirectoryName(target);
                if (targetParent is not null)
                {
                    Directory.CreateDirectory(targetParent);
                }

                await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await stream.WriteAsync(file.Content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(outputDirectory))
            {
                Directory.Move(temporaryDirectory, outputDirectory);
                destinationReplaced = true;
                return;
            }

            Directory.Move(outputDirectory, backupDirectory);
            try
            {
                Directory.Move(temporaryDirectory, outputDirectory);
                destinationReplaced = true;
            }
            catch
            {
                if (!Directory.Exists(outputDirectory) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, outputDirectory);
                }

                throw;
            }

            Directory.Delete(backupDirectory, recursive: true);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            if (destinationReplaced && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private static void ValidateHtmlBookOutputPath(string documentPath, string outputDirectory)
    {
        var root = Path.GetPathRoot(outputDirectory);
        if (root is not null
            && PathsEqual(
                Path.TrimEndingDirectorySeparator(outputDirectory),
                Path.TrimEndingDirectorySeparator(root)))
        {
            throw new ArgumentException("The HTML book output cannot be a filesystem root.", nameof(outputDirectory));
        }

        if (File.Exists(outputDirectory))
        {
            throw new ArgumentException("The HTML book output path points to an existing file.", nameof(outputDirectory));
        }

        var outputPrefix = Path.TrimEndingDirectorySeparator(outputDirectory) + Path.DirectorySeparatorChar;
        if (documentPath.StartsWith(
                outputPrefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("The HTML book output cannot contain its source document.", nameof(outputDirectory));
        }

        if (Directory.Exists(outputDirectory)
            && (File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("The HTML book output cannot be a symbolic link or reparse point.", nameof(outputDirectory));
        }
    }

    private static void ValidateReplaceableHtmlBookDirectory(string outputDirectory)
    {
        if (ContainsReparsePoint(outputDirectory))
        {
            throw new IOException("An existing HTML book directory contains a symbolic link or reparse point.");
        }

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new IOException("An existing output directory is not a replaceable Flow HTML book.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            if (manifest.RootElement.GetProperty("format").GetString() != HtmlBookPackage.Format)
            {
                throw new IOException("An existing output directory is not a replaceable Flow HTML book.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new IOException("An existing output directory has an invalid Flow HTML book manifest.", exception);
        }
    }

    private static bool ContainsReparsePoint(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop(), "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
            }
        }

        return false;
    }

    private static string ResolvePackageOutputPath(string root, string packagePath)
    {
        var target = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(
                prefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException($"Unsafe HTML book package path '{packagePath}'.");
        }

        return target;
    }

    private static async Task WriteEpubDiagnosticsAsync(
        IEnumerable<EpubDiagnostic> diagnostics,
        TextWriter output,
        TextWriter error,
        bool detailsPersisted = false)
    {
        const int maximumPersistedDetailsOnConsole = 40;
        var items = diagnostics.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var information = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Information);
        var warnings = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Warning);
        var errors = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Error);
        await output.WriteLineAsync(
                $"Diagnostics: {information.ToString(CultureInfo.InvariantCulture)} information, "
                + $"{warnings.ToString(CultureInfo.InvariantCulture)} warnings, "
                + $"{errors.ToString(CultureInfo.InvariantCulture)} errors.")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                "Diagnostic codes: " + string.Join(
                    ", ",
                    items.GroupBy(static item => item.Code, StringComparer.Ordinal)
                        .OrderBy(static group => group.Key, StringComparer.Ordinal)
                        .Select(static group => $"{group.Key}={group.Count().ToString(CultureInfo.InvariantCulture)}")))
            .ConfigureAwait(false);

        var visible = items.AsEnumerable();
        if (detailsPersisted && items.Length > maximumPersistedDetailsOnConsole)
        {
            var errorItems = items.Where(static item => item.Severity == EpubDiagnosticSeverity.Error).ToArray();
            visible = errorItems.Concat(items
                .Where(static item => item.Severity != EpubDiagnosticSeverity.Error)
                .Take(Math.Max(0, maximumPersistedDetailsOnConsole - errorItems.Length)));
        }

        var visibleCount = 0;
        foreach (var diagnostic in visible)
        {
            visibleCount++;
            var resource = diagnostic.Resource is null ? string.Empty : $" [{diagnostic.Resource}]";
            var line = $"{diagnostic.Severity} {diagnostic.Code}{resource}: {diagnostic.Message}";
            var writer = diagnostic.Severity == EpubDiagnosticSeverity.Information ? output : error;
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        if (visibleCount < items.Length)
        {
            await output.WriteLineAsync(
                    $"Console detail limited to {visibleCount.ToString(CultureInfo.InvariantCulture)} of "
                    + $"{items.Length.ToString(CultureInfo.InvariantCulture)} diagnostics; the JSON report contains every item.")
                .ConfigureAwait(false);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task WriteHashAsync(TextWriter output, DocumentHash hash)
    {
        await output.WriteLineAsync($"Hash: {hash.Algorithm}:{hash.Hash}").ConfigureAwait(false);
        await output.WriteLineAsync($"Canonicalization: {hash.CanonicalizationVersion}").ConfigureAwait(false);
    }

    private static DeviceClass GetDeviceClass(double width) => width switch
    {
        < AdaptiveLayoutEngine.MediumViewportMinimumWidth => DeviceClass.Phone,
        < AdaptiveLayoutEngine.LargeViewportMinimumWidth => DeviceClass.Tablet,
        _ => DeviceClass.Desktop,
    };

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string CssNumber(double value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);

    private static int CountNodes<TNode>(FlowDocument document)
        where TNode : DocumentNode =>
        document.Index.Locations.Count(static location => location.Node is TNode);
}
