using System.Text;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubPrivateQualificationTests
{
    [Fact]
    public async Task Qualification_RunsEligibleCandidateTwiceWithoutLeakingIdentityOrPaths()
    {
        using var workspace = new QualificationWorkspace();
        using var epub = MinimalEpubFactory.Create(package: Package("Secret editorial title"));
        var sourcePath = workspace.WriteSource("identifying-name.epub", epub.ToArray());
        var before = new FileInfo(sourcePath);
        var service = new EpubPrivateQualificationService();

        var first = await service.QualifyAsync(
            workspace.SourceRoot,
            workspace.RepositoryRoot,
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true));
        var second = await service.QualifyAsync(
            workspace.SourceRoot,
            workspace.RepositoryRoot,
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true));
        var firstJson = EpubPrivateQualificationReportJsonSerializer.Serialize(first);
        var secondJson = EpubPrivateQualificationReportJsonSerializer.Serialize(second);
        var json = Encoding.UTF8.GetString(firstJson);
        var after = new FileInfo(sourcePath);

        Assert.Equal(firstJson, secondJson);
        Assert.True(first.DeterministicAcrossRepeatedRuns);
        Assert.Equal(1, first.Summary.Total);
        Assert.Equal(1, first.Summary.Eligible);
        Assert.Equal(1, first.Summary.Passed);
        var publication = Assert.Single(first.Publications);
        Assert.Equal(EpubPrivateQualificationStatus.Passed, publication.Status);
        Assert.True(publication.StableAcrossRepeatedRuns);
        Assert.Equal(2, publication.Evidence.HtmlPackageCount);
        Assert.True(publication.Evidence.ImportedNodeCount > 0);
        Assert.NotNull(publication.Evidence.CanonicalHash);
        Assert.Contains(EpubCorpusExecutionPhase.Integrity, publication.CompletedPhases);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.LastWriteTimeUtc, after.LastWriteTimeUtc);
        Assert.DoesNotContain(workspace.SourceRoot, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identifying-name", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret editorial title", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-editorial-id", json, StringComparison.Ordinal);
        Assert.False(firstJson.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal((byte)'\n', firstJson[^1]);
    }

    [Fact]
    public async Task Qualification_ReportsProtectedCandidateAsSkippedWithoutImportingIt()
    {
        using var workspace = new QualificationWorkspace();
        using var epub = MinimalEpubFactory.Create(additionalTextEntries: new Dictionary<string, string>
        {
            ["META-INF/encryption.xml"] = """
                <?xml version="1.0" encoding="utf-8"?>
                <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                            xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
                  <enc:EncryptedData>
                    <enc:EncryptionMethod Algorithm="urn:vendor:protected" />
                    <enc:CipherData><enc:CipherReference URI="EPUB/text/chapter-1.xhtml" /></enc:CipherData>
                  </enc:EncryptedData>
                </encryption>
                """,
        });
        workspace.WriteSource("protected.epub", epub.ToArray());

        var report = await new EpubPrivateQualificationService().QualifyAsync(
            workspace.SourceRoot,
            workspace.RepositoryRoot,
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true));

        Assert.Equal(0, report.Summary.Eligible);
        Assert.Equal(1, report.Summary.Skipped);
        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateQualificationStatus.SkippedProtected, publication.Status);
        Assert.Empty(publication.CompletedPhases);
        Assert.Equal(0, publication.Evidence.ImportedNodeCount);
        Assert.Contains(publication.Diagnostics, static item =>
            item.Code == EpubPrivateInventoryDiagnosticCodes.UnsupportedEncryption
            && item.Phase is null);
    }

    [Fact]
    public async Task Qualification_FailsCandidateWhenFidelityReportsLostUnits()
    {
        using var workspace = new QualificationWorkspace();
        using var epub = MinimalEpubFactory.Create(chapterOne: """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" lang="en">
              <head><title>Loss fixture</title></head>
              <body><h1>Loss fixture</h1><p>Before<img src="../images/missing.png" alt="Missing" />after.</p></body>
            </html>
            """);
        workspace.WriteSource("loss.epub", epub.ToArray());

        var report = await new EpubPrivateQualificationService().QualifyAsync(
            workspace.SourceRoot,
            workspace.RepositoryRoot,
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true));

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateQualificationStatus.Failed, publication.Status);
        Assert.True(publication.Evidence.FidelityLostUnitCount > 0);
        Assert.Equal(1, report.Summary.Failed);
    }

    [Fact]
    public void Qualification_RequiresBothLocalUseDeclarations()
    {
        Assert.Throws<ArgumentException>(() =>
            new EpubPrivateQualificationOptions(legalUseDeclared: false, drmFreeDeclared: true));
        Assert.Throws<ArgumentException>(() =>
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: false));
    }

    [Fact]
    public async Task Qualification_RejectsPrivateSourceInsideRepository()
    {
        using var workspace = new QualificationWorkspace();
        var unsafeSource = Directory.CreateDirectory(Path.Combine(workspace.RepositoryRoot, "private-books")).FullName;

        await Assert.ThrowsAsync<ArgumentException>(() => new EpubPrivateQualificationService().QualifyAsync(
            unsafeSource,
            workspace.RepositoryRoot,
            new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true)));
    }

    private static string Package(string title) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">secret-editorial-id</dc:identifier>
            <dc:title>{{title}}</dc:title>
            <dc:language>pt-BR</dc:language>
          </metadata>
          <manifest>
            <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
            <item id="cover-image" href="images/flow.png" media-type="image/png" />
          </manifest>
          <spine><itemref idref="chapter-one" /><itemref idref="chapter-two" /></spine>
        </package>
        """;

    private sealed class QualificationWorkspace : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"flow-private-qualification-{Guid.NewGuid():N}");

        internal QualificationWorkspace()
        {
            SourceRoot = Directory.CreateDirectory(Path.Combine(root, "private-books")).FullName;
            RepositoryRoot = Directory.CreateDirectory(Path.Combine(root, "repository")).FullName;
        }

        internal string SourceRoot { get; }

        internal string RepositoryRoot { get; }

        internal string WriteSource(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(SourceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
