using System.Globalization;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering;
using Flow.Security;

namespace Flow.Cli;

/// <summary>Executes typed CLI commands against the Flow domain services.</summary>
public sealed class CliOperations
{
    private readonly IFlowDocumentSerializer _serializer;
    private readonly DocumentValidator _validator;
    private readonly IDocumentIntegrityService _integrityService;
    private readonly ILayoutEngine _layoutEngine;
    private readonly IDocumentRenderer _htmlRenderer;

    public CliOperations(
        IFlowDocumentSerializer serializer,
        DocumentValidator validator,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IDocumentRenderer htmlRenderer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);

        _serializer = serializer;
        _validator = validator;
        _integrityService = integrityService;
        _layoutEngine = layoutEngine;
        _htmlRenderer = htmlRenderer;
    }

    /// <summary>Executes a parsed command and writes its normal output.</summary>
    public async Task<int> ExecuteAsync(
        CliCommand command,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        return command switch
        {
            HelpCommand => await ShowHelpAsync(output).ConfigureAwait(false),
            SampleCommand sample => await CreateSampleAsync(sample, output, cancellationToken).ConfigureAwait(false),
            InspectCommand inspect => await InspectAsync(inspect, output, cancellationToken).ConfigureAwait(false),
            ValidateCommand validate => await ValidateAsync(validate, output, cancellationToken).ConfigureAwait(false),
            HashCommand hash => await HashAsync(hash, output, cancellationToken).ConfigureAwait(false),
            RenderHtmlCommand render => await RenderHtmlAsync(render, output, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown CLI command."),
        };
    }

    private static async Task<int> ShowHelpAsync(TextWriter output)
    {
        await output.WriteLineAsync("Flow Engine .NET 0.1.0-rc.1 (experimental)").ConfigureAwait(false);
        await output.WriteLineAsync("The .flow.json format and all 0.x APIs may change.").ConfigureAwait(false);
        await output.WriteLineAsync("Commands:").ConfigureAwait(false);
        await output.WriteLineAsync("  flow sample [output]                         Create the reference .flow.json book.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow inspect <document>                      Show semantic document counts.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow validate <document>                     Validate semantic invariants.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow hash <document>                         Compute the canonical SHA-256 hash.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("  flow render <document> --html <output> --width <n> --height <n>")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Exit codes: 0 success, 1 command/input failure, 2 semantic validation failure.")
            .ConfigureAwait(false);
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

    private async Task<FlowDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

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
