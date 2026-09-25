using System.IO.Compression;
using System.Text;
using Flow.Application;
using Flow.Documents;
using Flow.Rendering.Html;

namespace Flow.Application.Tests;

public sealed class FlowApplicationServiceTests
{
    [Fact]
    public async Task InspectAndImportRunWithoutConsoleOrOutputPath()
    {
        var service = FlowApplicationService.CreateDefault();
        var progress = new ProgressCollector();
        using var inspectionSource = CreateMinimalEpub();

        var inspection = await service.InspectEpubAsync(
            new InspectEpubRequest(inspectionSource),
            progress);

        Assert.True(inspection.Inspection.IsSuccess);
        Assert.Contains(progress.Updates, update =>
            update.Operation == FlowApplicationOperation.InspectEpub
            && update.Stage == FlowApplicationProgressStage.Completed);

        using var importSource = CreateMinimalEpub();
        var import = await service.ImportEpubAsync(new ImportEpubRequest(importSource), progress);

        Assert.True(import.Import.IsSuccess);
        Assert.NotNull(import.Import.Document);
        Assert.NotNull(import.Validation);
        Assert.True(import.Validation.IsValid);
        Assert.NotNull(import.Hash);
    }

    [Fact]
    public async Task DocumentUseCasesReturnArtifactsWithoutWritingFiles()
    {
        var service = FlowApplicationService.CreateDefault();
        var document = await ReadSampleAsync();
        var progress = new ProgressCollector();

        var validation = await service.ValidateDocumentAsync(new ValidateDocumentRequest(document), progress);
        var hash = await service.CalculateDocumentHashAsync(new CalculateDocumentHashRequest(document), progress);
        var html = await service.RenderHtmlAsync(new RenderHtmlRequest(document, 390, 844), progress);
        var book = await service.RenderHtmlBookAsync(
            new RenderHtmlBookRequest(document, uiLanguage: HtmlBookUiLanguage.PortugueseBrazil),
            progress);

        Assert.True(validation.Validation.IsValid);
        Assert.Equal(hash.Hash, html.Hash);
        Assert.Equal(hash.Hash, book.Hash);
        Assert.Equal("text/html; charset=utf-8", html.RenderedDocument.MediaType);
        Assert.Equal("index.html", book.Package.GetFile("index.html").Path);
        Assert.Contains(progress.Updates, update => update.Stage == FlowApplicationProgressStage.LayingOut);
        Assert.Contains(progress.Updates, update => update.Stage == FlowApplicationProgressStage.Rendering);
    }

    [Fact]
    public async Task CancellationIsObservedBeforeWorkStarts()
    {
        var service = FlowApplicationService.CreateDefault();
        var document = await ReadSampleAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RenderHtmlAsync(
            new RenderHtmlRequest(document, 390, 844),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ApplicationAssemblyDoesNotReferenceCliOrTerminalUi()
    {
        var references = typeof(IFlowApplicationService).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Flow.Cli", references);
        Assert.DoesNotContain("Spectre.Console", references);
        Assert.DoesNotContain(references, name => name is not null && name.Contains("WinUI", StringComparison.Ordinal));
    }

    private static async Task<FlowDocument> ReadSampleAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SampleBook", "sample.flow.json");
        await using var stream = File.OpenRead(path);
        return await new FlowJsonDocumentSerializer().DeserializeAsync(stream);
    }

    private static MemoryStream CreateMinimalEpub()
    {
        const string container = """
            <?xml version="1.0" encoding="utf-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" />
              </rootfiles>
            </container>
            """;
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:application-test</dc:identifier>
                <dc:title>Application boundary</dc:title>
                <dc:language>en-US</dc:language>
              </metadata>
              <manifest>
                <item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """;
        const string chapter = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" lang="en-US">
              <head><title>Chapter</title></head>
              <body><h1 id="start">Chapter</h1><p>Shared application boundary.</p></body>
            </html>
            """;
        var result = new MemoryStream();
        using (var archive = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "mimetype", "application/epub+zip");
            AddText(archive, "META-INF/container.xml", container);
            AddText(archive, "EPUB/package.opf", package);
            AddText(archive, "EPUB/chapter.xhtml", chapter);
        }

        result.Position = 0;
        return result;
    }

    private static void AddText(ZipArchive archive, string path, string value)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    private sealed class ProgressCollector : IProgress<FlowApplicationProgress>
    {
        public List<FlowApplicationProgress> Updates { get; } = [];

        public void Report(FlowApplicationProgress value) => Updates.Add(value);
    }
}
