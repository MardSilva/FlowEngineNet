using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Flow.Application;
using Flow.Documents;
using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsOperationServiceTests
{
    [Fact]
    public async Task InspectionReadsEpubWithoutChangingSourceOrCreatingOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("Livro de inspeção.epub");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var service = FlowWindowsOperationService.CreateDefault();

        var result = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Inspect,
            source));

        Assert.True(result.Succeeded);
        Assert.Equal("Livro de teste", result.Summary?.Title);
        Assert.Equal("3.0", result.Summary?.EpubVersion);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Single(Directory.EnumerateFiles(workspace.Path));
    }

    [Fact]
    public async Task ImportWritesDocumentDiagnosticsAndHtmlBookAtomically()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        var service = FlowWindowsOperationService.CreateDefault();
        var output = service.SuggestDocumentOutputPath(source, "Isto é um livro");

        var result = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output,
            writeDiagnosticsReport: true,
            writeHtmlBook: true,
            htmlLanguage: FlowWindowsHtmlLanguage.PortugueseBrazil));

        Assert.True(result.Succeeded);
        Assert.Equal("isto_e_um_livro.flow.json", Path.GetFileName(result.DocumentOutputPath));
        Assert.True(File.Exists(result.DocumentOutputPath));
        Assert.True(File.Exists(result.DiagnosticsOutputPath));
        Assert.True(File.Exists(Path.Combine(result.HtmlBookOutputPath!, "index.html")));
        Assert.False(File.Exists(service.GetInterruptedOutputPath(result.DocumentOutputPath!)));
        Assert.False(Directory.Exists(service.GetInterruptedOutputPath(result.HtmlBookOutputPath!)));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(source)));

        await using var stream = File.OpenRead(result.DocumentOutputPath!);
        var document = await new FlowJsonDocumentSerializer().DeserializeAsync(stream);
        Assert.Equal("Livro de teste", document.Metadata.Title);
    }

    [Fact]
    public async Task ExistingOutputRequiresConfirmedReplacement()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var output = Path.Combine(workspace.Path, "saida.flow.json");
        await File.WriteAllTextAsync(output, "do not replace");
        var service = FlowWindowsOperationService.CreateDefault();

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output)));

        var replaced = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output,
            outputPolicy: FlowWindowsOutputPolicy.ReplaceConfirmed));

        Assert.True(replaced.Succeeded);
        Assert.DoesNotContain("do not replace", await File.ReadAllTextAsync(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingOptionalOutputFailsBeforeDocumentIsCreated()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var output = Path.Combine(workspace.Path, "saida.flow.json");
        var diagnostics = Path.ChangeExtension(output, ".diagnostics.json");
        await File.WriteAllTextAsync(diagnostics, "keep");
        var service = FlowWindowsOperationService.CreateDefault();

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output,
            writeDiagnosticsReport: true)));

        Assert.False(File.Exists(output));
        Assert.Equal("keep", await File.ReadAllTextAsync(diagnostics));
    }

    [Fact]
    public async Task ResumeRemovesOnlyRecognizedPartialAndCancellationLeavesNoOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var output = Path.Combine(workspace.Path, "saida.flow.json");
        var service = FlowWindowsOperationService.CreateDefault();
        var partial = service.GetInterruptedOutputPath(output);
        await File.WriteAllTextAsync(partial, "interrupted");

        var resumed = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output,
            outputPolicy: FlowWindowsOutputPolicy.ResumeInterrupted));

        Assert.True(resumed.Succeeded);
        Assert.False(File.Exists(partial));

        var canceledOutput = Path.Combine(workspace.Path, "cancelado.flow.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            new FlowWindowsOperationRequest(FlowWindowsOperationKind.Import, source, canceledOutput),
            cancellationToken: cancellation.Token));
        Assert.False(File.Exists(canceledOutput));
        Assert.False(File.Exists(service.GetInterruptedOutputPath(canceledOutput)));
    }

    [Fact]
    public async Task CancellationWhileStagingRemovesTheEntireOutputSet()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var output = Path.Combine(workspace.Path, "saida.flow.json");
        using var cancellation = new CancellationTokenSource();
        var serializer = new CancelAfterSerializeFlowDocumentSerializer(cancellation);
        var service = new FlowWindowsOperationService(FlowApplicationService.CreateDefault(), serializer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            new FlowWindowsOperationRequest(
                FlowWindowsOperationKind.Import,
                source,
                output,
                writeDiagnosticsReport: true,
                writeHtmlBook: true),
            cancellationToken: cancellation.Token));

        var diagnostics = Path.ChangeExtension(output, ".diagnostics.json");
        var html = Path.Combine(workspace.Path, "saida_book");
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(diagnostics));
        Assert.False(Directory.Exists(html));
        Assert.False(File.Exists(service.GetInterruptedOutputPath(output)));
        Assert.False(File.Exists(service.GetInterruptedOutputPath(diagnostics)));
        Assert.False(Directory.Exists(service.GetInterruptedOutputPath(html)));
    }

    [Fact]
    public async Task ValidationAcceptsEpubAndFlowWithoutWritingFiles()
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("entrada.epub");
        var service = FlowWindowsOperationService.CreateDefault();
        var import = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source));
        var countBefore = Directory.EnumerateFileSystemEntries(workspace.Path).Count();

        var epub = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Validate,
            source));
        var flow = await service.ExecuteAsync(new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Validate,
            import.DocumentOutputPath!));

        Assert.True(epub.Succeeded);
        Assert.True(flow.Succeeded);
        Assert.Equal(countBefore, Directory.EnumerateFileSystemEntries(workspace.Path).Count());
    }

    [Theory]
    [InlineData(FlowWindowsPreviewProfile.Phone)]
    [InlineData(FlowWindowsPreviewProfile.Tablet)]
    [InlineData(FlowWindowsPreviewProfile.Desktop)]
    public async Task PreviewIsLocalDisposableAndDoesNotChangeTheSource(FlowWindowsPreviewProfile profile)
    {
        using var workspace = new TemporaryWorkspace();
        var source = workspace.CreateEpub("preview.epub");
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var previewRoot = Path.Combine(workspace.Path, "private-preview");
        var service = new FlowWindowsOperationService(
            FlowApplicationService.CreateDefault(),
            new FlowJsonDocumentSerializer(),
            previewRoot);

        string previewPath;
        await using (var preview = await service.CreatePreviewAsync(source, profile))
        {
            var validation = await service.ExecuteAsync(new FlowWindowsOperationRequest(
                FlowWindowsOperationKind.Validate,
                source));
            previewPath = preview.RootPath;
            Assert.True(File.Exists(preview.IndexPath));
            Assert.Equal(profile, preview.Profile);
            Assert.Equal("Livro de teste", preview.Summary.Title);
            Assert.False(string.IsNullOrWhiteSpace(preview.DocumentHash));
            Assert.Equal(validation.DocumentHash, preview.DocumentHash);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        }

        Assert.False(Directory.Exists(previewPath));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-windows-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateEpub(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            using var file = File.Create(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            AddText(archive, "mimetype", "application/epub+zip");
            AddText(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" /></rootfiles>
                </container>
                """);
            AddText(archive, "EPUB/package.opf", """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">urn:flow:windows-test</dc:identifier>
                    <dc:title>Livro de teste</dc:title><dc:creator>Autora Exemplo</dc:creator><dc:language>pt-BR</dc:language>
                  </metadata>
                  <manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml" /></manifest>
                  <spine><itemref idref="chapter" /></spine>
                </package>
                """);
            AddText(archive, "EPUB/chapter.xhtml", """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" lang="pt-BR">
                  <head><title>Capítulo</title></head><body><h1 id="inicio">Capítulo</h1><p>Conteúdo.</p></body>
                </html>
                """);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static void AddText(ZipArchive archive, string path, string value)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(value));
        }
    }

    private sealed class CancelAfterSerializeFlowDocumentSerializer(CancellationTokenSource cancellation)
        : IFlowDocumentSerializer
    {
        private readonly FlowJsonDocumentSerializer _inner = new();

        public async Task SerializeAsync(
            FlowDocument document,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            await _inner.SerializeAsync(document, destination, cancellationToken);
            cancellation.Cancel();
        }

        public Task<FlowDocument> DeserializeAsync(
            Stream source,
            CancellationToken cancellationToken = default) =>
            _inner.DeserializeAsync(source, cancellationToken);
    }
}
