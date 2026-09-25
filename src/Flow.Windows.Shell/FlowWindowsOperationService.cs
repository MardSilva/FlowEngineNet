using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Application;
using Flow.Documents;
using Flow.Rendering.Html;

namespace Flow.Windows.Shell;

/// <summary>Coordinates local file workflows over the shared typed application service.</summary>
public sealed class FlowWindowsOperationService : IFlowWindowsOperationService
{
    private const int MaximumStemLength = 96;
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IFlowApplicationService _application;
    private readonly IFlowDocumentSerializer _serializer;

    public FlowWindowsOperationService(
        IFlowApplicationService application,
        IFlowDocumentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(serializer);
        _application = application;
        _serializer = serializer;
    }

    public static FlowWindowsOperationService CreateDefault() =>
        new(FlowApplicationService.CreateDefault(), new FlowJsonDocumentSerializer());

    public async Task<FlowWindowsOperationResult> ExecuteAsync(
        FlowWindowsOperationRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureReadableSource(request.SourcePath);
        return request.Operation switch
        {
            FlowWindowsOperationKind.Inspect => await InspectAsync(request, progress, cancellationToken)
                .ConfigureAwait(false),
            FlowWindowsOperationKind.Import => await ImportAsync(request, progress, cancellationToken)
                .ConfigureAwait(false),
            FlowWindowsOperationKind.Validate => await ValidateAsync(request, progress, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    public string SuggestDocumentOutputPath(string sourcePath, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var stem = Slug(title ?? string.Empty);
        if (stem.Length == 0)
        {
            stem = Slug(Path.GetFileNameWithoutExtension(sourcePath));
        }

        if (stem.Length == 0)
        {
            stem = "imported_book";
        }

        if (WindowsReservedNames.Contains(stem))
        {
            stem = $"book_{stem}";
        }

        return Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory(),
            $"{stem}.flow.json");
    }

    public string GetInterruptedOutputPath(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        var fullPath = Path.GetFullPath(finalPath);
        return Path.Combine(
            Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("The output must have a parent directory."),
            $".{Path.GetFileName(fullPath)}.flow-partial");
    }

    private async Task<FlowWindowsOperationResult> InspectAsync(
        FlowWindowsOperationRequest request,
        IProgress<FlowApplicationProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureExtension(request.SourcePath, ".epub");
        await using var source = OpenRead(request.SourcePath);
        var result = await _application.InspectEpubAsync(new InspectEpubRequest(source), progress, cancellationToken)
            .ConfigureAwait(false);
        var package = result.Inspection.Package;
        var summary = package is null
            ? null
            : new FlowWindowsBookSummary(
                package.Title ?? Path.GetFileNameWithoutExtension(request.SourcePath),
                package.Creators,
                package.Language,
                package.DeclaredVersion ?? package.VersionFamily.ToString(),
                result.Inspection.Resources.ManifestItemCount,
                result.Inspection.Spine.Length,
                0,
                null,
                []);
        return new FlowWindowsOperationResult(
            FlowWindowsOperationKind.Inspect,
            result.Inspection.IsSuccess,
            summary,
            result.Diagnostics);
    }

    private async Task<FlowWindowsOperationResult> ImportAsync(
        FlowWindowsOperationRequest request,
        IProgress<FlowApplicationProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureExtension(request.SourcePath, ".epub");
        await using var source = OpenRead(request.SourcePath);
        var result = await _application.ImportEpubAsync(new ImportEpubRequest(source), progress, cancellationToken)
            .ConfigureAwait(false);
        var document = result.Import.Document;
        var summary = document is null ? null : CreateSummary(document, null);
        if (!result.Import.IsSuccess || document is null || result.Validation?.IsValid != true)
        {
            return new FlowWindowsOperationResult(
                FlowWindowsOperationKind.Import,
                false,
                summary,
                result.Diagnostics);
        }

        var documentPath = request.DocumentOutputPath
                           ?? SuggestDocumentOutputPath(request.SourcePath, document.Metadata.Title);
        EnsureDifferentPaths(request.SourcePath, documentPath);
        var diagnosticsPath = request.WriteDiagnosticsReport
            ? Path.ChangeExtension(documentPath, ".diagnostics.json")
            : null;
        var htmlPath = request.WriteHtmlBook
            ? Path.Combine(
                Path.GetDirectoryName(documentPath)!,
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(documentPath)) + "_book")
            : null;
        ValidateDestination(documentPath, request.OutputPolicy, isDirectory: false);
        if (diagnosticsPath is not null)
        {
            ValidateDestination(diagnosticsPath, request.OutputPolicy, isDirectory: false);
        }

        if (htmlPath is not null)
        {
            ValidateDestination(htmlPath, request.OutputPolicy, isDirectory: true);
        }

        var outputs = new List<StagedOutput>
        {
            CreateStagedOutput(documentPath, isDirectory: false),
        };
        if (diagnosticsPath is not null)
        {
            outputs.Add(CreateStagedOutput(diagnosticsPath, isDirectory: false));
        }

        if (htmlPath is not null)
        {
            outputs.Add(CreateStagedOutput(htmlPath, isDirectory: true));
        }

        PrepareStaging(outputs, request.OutputPolicy);
        try
        {
            await StageFileAsync(
                    outputs[0],
                    (stream, token) => _serializer.SerializeAsync(document, stream, token),
                    cancellationToken)
                .ConfigureAwait(false);

            var outputIndex = 1;
            if (request.WriteDiagnosticsReport)
            {
                await StageDiagnosticsAsync(
                        outputs[outputIndex++],
                        result.Diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (request.WriteHtmlBook)
            {
                var rendered = await _application.RenderHtmlBookAsync(
                        new RenderHtmlBookRequest(document, uiLanguage: MapLanguage(request.HtmlLanguage)),
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                await StagePackageAsync(rendered.Package, outputs[outputIndex], cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CommitStagedOutputs(outputs);
        }
        catch
        {
            RollBackStagedOutputs(outputs);
            throw;
        }

        return new FlowWindowsOperationResult(
            FlowWindowsOperationKind.Import,
            true,
            summary,
            result.Diagnostics,
            documentPath,
            diagnosticsPath,
            htmlPath,
            result.Hash?.Hash);
    }

    private async Task<FlowWindowsOperationResult> ValidateAsync(
        FlowWindowsOperationRequest request,
        IProgress<FlowApplicationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(request.SourcePath);
        if (extension.Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            await using var source = OpenRead(request.SourcePath);
            var imported = await _application.ImportEpubAsync(
                    new ImportEpubRequest(source),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            return new FlowWindowsOperationResult(
                FlowWindowsOperationKind.Validate,
                imported.Import.IsSuccess && imported.Validation?.IsValid == true,
                imported.Import.Document is null ? null : CreateSummary(imported.Import.Document, null),
                imported.Diagnostics,
                DocumentHash: imported.Hash?.Hash);
        }

        if (!request.SourcePath.EndsWith(".flow.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Validation accepts local .epub or .flow.json files.");
        }

        await using var documentSource = OpenRead(request.SourcePath);
        var document = await _serializer.DeserializeAsync(documentSource, cancellationToken).ConfigureAwait(false);
        var validated = await _application.ValidateDocumentAsync(
                new ValidateDocumentRequest(document),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return new FlowWindowsOperationResult(
            FlowWindowsOperationKind.Validate,
            validated.Validation.IsValid,
            CreateSummary(document, null),
            validated.Diagnostics);
    }

    private static FlowWindowsBookSummary CreateSummary(FlowDocument document, string? epubVersion)
    {
        var coverBytes = ImmutableArray<byte>.Empty;
        string? coverMediaType = null;
        if (document.Presentation?.Cover is { } cover
            && document.Index.TryGetUniqueNode(cover.FigureId, out var node)
            && node is Figure figure
            && document.Assets.TryGetValue(figure.AssetId, out var asset))
        {
            coverBytes = asset.Data;
            coverMediaType = asset.MediaType;
        }

        return new FlowWindowsBookSummary(
            document.Metadata.Title,
            document.Metadata.Authors,
            document.Metadata.Language,
            epubVersion,
            document.Index.NodeCount,
            document.Content.Children.Length,
            document.Assets.Count,
            coverMediaType,
            coverBytes);
    }

    private static StagedOutput CreateStagedOutput(string finalPath, bool isDirectory)
    {
        var fullPath = Path.GetFullPath(finalPath);
        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new ArgumentException("The output must have a parent directory.", nameof(finalPath));
        return new StagedOutput(
            fullPath,
            Path.Combine(parent, $".{Path.GetFileName(fullPath)}.flow-partial"),
            Path.Combine(parent, $".{Path.GetFileName(fullPath)}.flow-backup"),
            isDirectory);
    }

    private static void PrepareStaging(
        IEnumerable<StagedOutput> outputs,
        FlowWindowsOutputPolicy policy)
    {
        foreach (var output in outputs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output.FinalPath)!);
            PrepareDestination(output.FinalPath, output.PartialPath, policy, output.IsDirectory);
            if (File.Exists(output.BackupPath) || Directory.Exists(output.BackupPath))
            {
                throw new IOException($"A previous output backup requires review: {output.BackupPath}");
            }
        }
    }

    private static async Task StageDiagnosticsAsync(
        StagedOutput output,
        ImmutableArray<FlowApplicationDiagnostic> diagnostics,
        CancellationToken cancellationToken) =>
        await StageFileAsync(
                output,
                async (stream, token) =>
                {
                    await JsonSerializer.SerializeAsync(
                            stream,
                            new { format = "flow-windows-diagnostics-0.1", diagnostics },
                            ReportJsonOptions,
                            token)
                        .ConfigureAwait(false);
                    await stream.WriteAsync("\n"u8.ToArray(), token).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task StageFileAsync(
        StagedOutput output,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
                         output.PartialPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await writer(stream, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task StagePackageAsync(
        HtmlBookPackage package,
        StagedOutput output,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output.PartialPath);
        foreach (var file in package.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(
                Path.Combine(output.PartialPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(
                    output.PartialPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The HTML package contains an unsafe path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, file.Content.ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void CommitStagedOutputs(IReadOnlyList<StagedOutput> outputs)
    {
        foreach (var output in outputs)
        {
            if (output.IsDirectory ? Directory.Exists(output.FinalPath) : File.Exists(output.FinalPath))
            {
                Move(output.FinalPath, output.BackupPath, output.IsDirectory);
                output.BackupCreated = true;
            }

            Move(output.PartialPath, output.FinalPath, output.IsDirectory);
            output.Committed = true;
        }

        foreach (var output in outputs)
        {
            Delete(output.BackupPath, output.IsDirectory);
            output.BackupCreated = false;
        }
    }

    private static void RollBackStagedOutputs(IEnumerable<StagedOutput> outputs)
    {
        foreach (var output in outputs.Reverse())
        {
            if (output.Committed)
            {
                Delete(output.FinalPath, output.IsDirectory);
            }

            if (output.BackupCreated
                && (output.IsDirectory ? Directory.Exists(output.BackupPath) : File.Exists(output.BackupPath)))
            {
                Move(output.BackupPath, output.FinalPath, output.IsDirectory);
            }

            Delete(output.PartialPath, output.IsDirectory);
        }
    }

    private static void Move(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static void Delete(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void PrepareDestination(
        string finalPath,
        string partialPath,
        FlowWindowsOutputPolicy policy,
        bool isDirectory)
    {
        var finalExists = isDirectory ? Directory.Exists(finalPath) : File.Exists(finalPath);
        var partialExists = isDirectory ? Directory.Exists(partialPath) : File.Exists(partialPath);
        if (finalExists && policy != FlowWindowsOutputPolicy.ReplaceConfirmed)
        {
            throw new IOException($"The output already exists: {finalPath}");
        }

        if (partialExists && policy is not (FlowWindowsOutputPolicy.ResumeInterrupted or FlowWindowsOutputPolicy.ReplaceConfirmed))
        {
            throw new IOException($"An interrupted output exists: {partialPath}");
        }

        if (partialExists)
        {
            if (isDirectory)
            {
                Directory.Delete(partialPath, recursive: true);
            }
            else
            {
                File.Delete(partialPath);
            }
        }
    }

    private void ValidateDestination(
        string finalPath,
        FlowWindowsOutputPolicy policy,
        bool isDirectory)
    {
        var finalExists = isDirectory ? Directory.Exists(finalPath) : File.Exists(finalPath);
        var partialPath = GetInterruptedOutputPath(finalPath);
        var partialExists = isDirectory ? Directory.Exists(partialPath) : File.Exists(partialPath);
        if (finalExists && policy != FlowWindowsOutputPolicy.ReplaceConfirmed)
        {
            throw new IOException($"The output already exists: {finalPath}");
        }

        if (partialExists && policy is not (FlowWindowsOutputPolicy.ResumeInterrupted or FlowWindowsOutputPolicy.ReplaceConfirmed))
        {
            throw new IOException($"An interrupted output exists: {partialPath}");
        }
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void EnsureReadableSource(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected document was not found.", path);
        }
    }

    private static void EnsureExtension(string path, string extension)
    {
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"This operation requires a {extension} file.");
        }
    }

    private static void EnsureDifferentPaths(string sourcePath, string outputPath)
    {
        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The output cannot replace the selected source file.");
        }
    }

    private static HtmlBookUiLanguage MapLanguage(FlowWindowsHtmlLanguage value) => value switch
    {
        FlowWindowsHtmlLanguage.Automatic => HtmlBookUiLanguage.Automatic,
        FlowWindowsHtmlLanguage.English => HtmlBookUiLanguage.English,
        FlowWindowsHtmlLanguage.PortugueseBrazil => HtmlBookUiLanguage.PortugueseBrazil,
        FlowWindowsHtmlLanguage.PortuguesePortugal => HtmlBookUiLanguage.PortuguesePortugal,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Slug(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaximumStemLength));
        var separatorPending = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separatorPending && builder.Length > 0 && builder.Length < MaximumStemLength)
                {
                    builder.Append('_');
                }

                if (builder.Length < MaximumStemLength)
                {
                    builder.Append(lower);
                }

                separatorPending = false;
            }
            else if (builder.Length > 0)
            {
                separatorPending = true;
            }

            if (builder.Length >= MaximumStemLength)
            {
                break;
            }
        }

        return builder.ToString().TrimEnd('_');
    }

    private sealed class StagedOutput(
        string finalPath,
        string partialPath,
        string backupPath,
        bool isDirectory)
    {
        public string FinalPath { get; } = finalPath;

        public string PartialPath { get; } = partialPath;

        public string BackupPath { get; } = backupPath;

        public bool IsDirectory { get; } = isDirectory;

        public bool BackupCreated { get; set; }

        public bool Committed { get; set; }
    }
}
