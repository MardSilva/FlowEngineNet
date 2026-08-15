using System.Xml.Linq;
using Flow.Cli;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Cli.Tests;

public sealed class FlowCliIntegrationTests
{
    [Fact]
    public async Task CommittedSample_IsValidAndContainsTheRequiredBookStructure()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "SampleBook", "sample.flow.json");
        await using var stream = File.OpenRead(samplePath);
        var document = await new FlowJsonDocumentSerializer().DeserializeAsync(stream);

        Assert.Equal("The Flow Experiment", document.Metadata.Title);
        Assert.Equal(5, document.Content.Children.Count(static node => node is Chapter));
        Assert.Contains(document.Index.Locations, static location => location.Node is TableOfContents);
        Assert.Contains(document.Index.Locations, static location => location.Node is Figure { Caption: not null });
        Assert.Contains(document.Index.Locations, static location => location.Node is Footnote);
        Assert.Contains(document.Index.Locations, static location => location.Node is CodeBlock);
        Assert.True(new DocumentValidator().Validate(document).IsValid);
    }

    [Fact]
    public async Task CommittedArtifacts_AreDeterministicOutputsOfTheCurrentPipeline()
    {
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "SampleBook");
        var samplePath = Path.Combine(sampleDirectory, "sample.flow.json");
        var serializer = new FlowJsonDocumentSerializer();
        await using var source = File.OpenRead(samplePath);
        var document = await serializer.DeserializeAsync(source);
        await using var regeneratedSource = new MemoryStream();
        await serializer.SerializeAsync(SampleBookFactory.Create(), regeneratedSource);

        Assert.True(File.ReadAllBytes(samplePath).AsSpan().SequenceEqual(regeneratedSource.ToArray()));

        var renderer = new HtmlDocumentRenderer();
        var preferences = new UserReadingPreferences();
        var mobileLayout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var desktopLayout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(1600, 1000, DeviceClass.Desktop, userPreferences: preferences));

        Assert.True(
            File.ReadAllBytes(Path.Combine(sampleDirectory, "mobile.html"))
                .AsSpan()
                .SequenceEqual(renderer.Render(document, mobileLayout, preferences).Content.AsSpan()));
        Assert.True(
            File.ReadAllBytes(Path.Combine(sampleDirectory, "desktop.html"))
                .AsSpan()
                .SequenceEqual(renderer.Render(document, desktopLayout, preferences).Content.AsSpan()));
    }

    [Fact]
    public async Task SampleInspectValidateAndHash_OperateOnTheSameGeneratedDocument()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("generated.flow.json");
        var application = FlowCliApplication.CreateDefault();

        var sample = await RunAsync(application, ["sample", documentPath]);
        var inspect = await RunAsync(application, ["inspect", documentPath]);
        var validate = await RunAsync(application, ["validate", documentPath]);
        var hash = await RunAsync(application, ["hash", documentPath]);

        Assert.Equal(0, sample.ExitCode);
        Assert.True(File.Exists(documentPath));
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains($"ID: {SampleBookFactory.DocumentUrn}", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Chapters: 5", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Sections: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Paragraphs: 18", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Figures: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Footnotes: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Anchors: 44", inspect.Output, StringComparison.Ordinal);
        Assert.Equal((0, "Valid: no semantic validation errors."), (validate.ExitCode, validate.Output.Trim()));
        Assert.Equal(0, hash.ExitCode);
        Assert.Contains("Hash: SHA-256:", hash.Output, StringComparison.Ordinal);
        Assert.Contains("Canonicalization: flow-c14n-0.1", hash.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_ReturnsTwoAndDiagnosticsForInvalidDocument()
    {
        using var workspace = new TemporaryWorkspace();
        var path = workspace.PathOf("invalid.flow.json");
        var duplicateId = new NodeId("duplicate");
        var invalid = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:cli:invalid")),
            new DocumentMetadata("Invalid"),
            new DocumentContent(
            [
                new Paragraph(duplicateId, [new Text("One")]),
                new Paragraph(duplicateId, [new Text("Two")]),
            ]));
        await using (var stream = File.Create(path))
        {
            await new FlowJsonDocumentSerializer().SerializeAsync(invalid, stream);
        }

        var result = await RunAsync(FlowCliApplication.CreateDefault(), ["validate", path]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(ValidationDiagnosticCodes.DuplicateNodeId, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_MobileAndDesktopPreserveIdentityHashAndAnchors()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var mobilePath = workspace.PathOf("mobile.html");
        var desktopPath = workspace.PathOf("desktop.html");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);

        var mobile = await RunAsync(
            application,
            ["render", documentPath, "--html", mobilePath, "--width", "390", "--height", "844"]);
        var desktop = await RunAsync(
            application,
            ["render", documentPath, "--html", desktopPath, "--width", "1600", "--height", "1000"]);

        Assert.Equal(0, mobile.ExitCode);
        Assert.Equal(0, desktop.ExitCode);
        Assert.True(File.Exists(mobilePath));
        Assert.True(File.Exists(desktopPath));

        var mobileHtml = XDocument.Load(mobilePath);
        var desktopHtml = XDocument.Load(desktopPath);
        Assert.Equal(SampleBookFactory.DocumentUrn, DocumentId(mobileHtml));
        Assert.Equal(DocumentId(mobileHtml), DocumentId(desktopHtml));
        Assert.Equal(OutputValue(mobile.Output, "Hash: "), OutputValue(desktop.Output, "Hash: "));
        Assert.Equal(OutputValue(mobile.Output, "Anchors: "), OutputValue(desktop.Output, "Anchors: "));
        Assert.Equal(ElementIds(mobileHtml), ElementIds(desktopHtml));
        Assert.Equal(AnchorTargets(mobileHtml), AnchorTargets(desktopHtml));
        Assert.Contains("Viewport: 390x844 (Small)", mobile.Output, StringComparison.Ordinal);
        Assert.Contains("Viewport: 1600x1000 (Large)", desktop.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_ReportsParsingAndFileErrorsWithoutThrowing()
    {
        using var workspace = new TemporaryWorkspace();
        var application = FlowCliApplication.CreateDefault();

        var parseError = await RunAsync(application, ["unknown"]);
        var fileError = await RunAsync(application, ["inspect", workspace.PathOf("missing.flow.json")]);

        Assert.Equal(1, parseError.ExitCode);
        Assert.Contains("Unknown command", parseError.Error, StringComparison.Ordinal);
        Assert.Equal(1, fileError.ExitCode);
        Assert.Contains("FLOWCLI_OPERATION_FAILED:", fileError.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_StatesExperimentalStatusCommandsAndExitCodes()
    {
        var result = await RunAsync(FlowCliApplication.CreateDefault(), ["help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0.1.0-rc.1 (experimental)", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow validate <document>", result.Output, StringComparison.Ordinal);
        Assert.Contains("Exit codes: 0 success, 1 command/input failure, 2 semantic validation failure.", result.Output, StringComparison.Ordinal);
    }

    private static async Task<CliResult> RunAsync(FlowCliApplication application, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await application.RunAsync(arguments, output, error);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private static string? DocumentId(XDocument document) =>
        (string?)document.Descendants("article").Single().Attribute("data-document-id");

    private static string[] ElementIds(XDocument document) =>
        document.Descendants()
            .Attributes("id")
            .Select(static attribute => attribute.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] AnchorTargets(XDocument document) =>
        document.Descendants("a")
            .Attributes("href")
            .Select(static attribute => attribute.Value)
            .Where(static value => value.StartsWith('#'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string OutputValue(string output, string prefix) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Trim();

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-cli-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string fileName) => Path.Combine(Root, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
