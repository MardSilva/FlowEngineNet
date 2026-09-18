using System.Text;
using System.Text.Json;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubPrivateInventoryTests
{
    [Fact]
    public async Task Inventory_IsReadOnlyNeutralDeterministicAndDeduplicated()
    {
        using var workspace = new InventoryWorkspace();
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">private-identifier</dc:identifier>
                <dc:title>Private title that must not leak</dc:title>
                <dc:language>pt-BR</dc:language>
                <dc:language>en-US</dc:language>
                <dc:creator>Private author</dc:creator>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" />
              </manifest>
              <spine><itemref idref="chapter-one" /><itemref idref="chapter-two" /></spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(package: package);
        var bytes = epub.ToArray();
        var firstPath = workspace.Write("private-name.epub", bytes);
        _ = workspace.Write("nested/copy.epub", bytes);
        File.SetLastWriteTimeUtc(firstPath, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var before = new FileInfo(firstPath);
        var service = new EpubPrivateInventoryService();

        var first = await service.InventoryAsync(workspace.Root);
        var second = await service.InventoryAsync(workspace.Root);
        var after = new FileInfo(firstPath);
        var firstJson = EpubPrivateInventoryReportJsonSerializer.Serialize(first);
        var secondJson = EpubPrivateInventoryReportJsonSerializer.Serialize(second);
        var text = Encoding.UTF8.GetString(firstJson);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(2, first.Summary.DiscoveredFiles);
        var publication = Assert.Single(first.Publications);
        Assert.Equal(2, publication.CopyCount);
        Assert.Equal(EpubPrivateInventoryStatus.Ready, publication.Status);
        Assert.Equal(EpubVersionFamily.Epub3, publication.EpubVersion);
        Assert.Equal(["en-US", "pt-BR"], publication.Languages.ToArray());
        Assert.NotNull(publication.Sha256);
        Assert.Equal((long)bytes.Length, publication.FileBytes);
        Assert.Equal(3, publication.Resources.ManifestItems);
        Assert.Equal(2, publication.Resources.Xhtml);
        Assert.Equal(1, publication.Resources.RasterImages);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.LastWriteTimeUtc, after.LastWriteTimeUtc);
        Assert.DoesNotContain(workspace.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-name", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private title", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Private author", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-identifier", text, StringComparison.Ordinal);
        Assert.False(firstJson.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal((byte)'\n', firstJson[^1]);
    }

    [Fact]
    public async Task Inventory_IdentifiesEpub2()
    {
        using var workspace = new InventoryWorkspace();
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">epub-two</dc:identifier>
                <dc:title>EPUB 2</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine><itemref idref="chapter-one" /><itemref idref="chapter-two" /></spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(package: package, includeImage: false);
        workspace.Write("epub2.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubVersionFamily.Epub2, publication.EpubVersion);
        Assert.Equal(["en"], publication.Languages.ToArray());
    }

    [Fact]
    public async Task Inventory_SeparatesProtectedCorruptAndUnsuitableCandidates()
    {
        using var workspace = new InventoryWorkspace();
        using var protectedEpub = MinimalEpubFactory.Create(additionalTextEntries: new Dictionary<string, string>
        {
            ["META-INF/encryption.xml"] = Encryption("urn:vendor:unsupported-encryption"),
        });
        workspace.Write("protected.epub", protectedEpub.ToArray());
        workspace.Write("corrupt.epub", [0x50, 0x4B, 0x03, 0x04, 0x01]);
        using var unsuitableEpub = MinimalEpubFactory.Create(package: """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="4.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">unknown</dc:identifier><dc:title>Unknown</dc:title><dc:language>zxx</dc:language>
              </metadata>
              <manifest><item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" /></manifest>
              <spine><itemref idref="chapter-one" /></spine>
            </package>
            """);
        workspace.Write("unsuitable.epub", unsuitableEpub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        Assert.Equal(3, report.Summary.DiscoveredFiles);
        Assert.Equal(1, report.Summary.Protected);
        Assert.Equal(1, report.Summary.Corrupt);
        Assert.Equal(1, report.Summary.Unsuitable);
        Assert.Contains(report.Publications, static item =>
            item.Status == EpubPrivateInventoryStatus.Protected
            && item.Protection == EpubPrivateInventoryProtectionStatus.UnsupportedEncryption
            && item.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == EpubPrivateInventoryDiagnosticCodes.UnsupportedEncryption));
    }

    [Theory]
    [InlineData("http://www.idpf.org/2008/embedding")]
    [InlineData("http://ns.adobe.com/pdf/enc#RC")]
    public async Task Inventory_TreatsKnownFontObfuscationAsReviewInsteadOfDrm(string algorithm)
    {
        using var workspace = new InventoryWorkspace();
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">font-test</dc:identifier>
                <dc:title>Font test</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" />
                <item id="font" href="fonts/book.otf" media-type="font/otf" />
              </manifest>
              <spine><itemref idref="chapter-one" /><itemref idref="chapter-two" /></spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(
            package: package,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["META-INF/encryption.xml"] = Encryption(algorithm),
            },
            additionalBinaryEntries: new Dictionary<string, byte[]>
            {
                ["EPUB/fonts/book.otf"] = [0x4F, 0x54, 0x54, 0x4F],
            });
        workspace.Write("font-obfuscation.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateInventoryStatus.ReviewRequired, publication.Status);
        Assert.Equal(EpubPrivateInventoryProtectionStatus.FontObfuscationOnly, publication.Protection);
    }

    [Fact]
    public async Task Inventory_DoesNotTreatKnownAlgorithmOnNonFontAsFontObfuscation()
    {
        using var workspace = new InventoryWorkspace();
        using var epub = MinimalEpubFactory.Create(additionalTextEntries: new Dictionary<string, string>
        {
            ["META-INF/encryption.xml"] = Encryption("http://www.idpf.org/2008/embedding"),
        });
        workspace.Write("misapplied-obfuscation.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateInventoryStatus.Protected, publication.Status);
        Assert.Equal(EpubPrivateInventoryProtectionStatus.UnsupportedEncryption, publication.Protection);
    }

    [Fact]
    public async Task Inventory_CountsAndRecognizesObfuscatedLegacyTrueTypeMediaType()
    {
        using var workspace = new InventoryWorkspace();
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">legacy-font-test</dc:identifier>
                <dc:title>Legacy font test</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="font" href="fonts/book.ttf" media-type="application/x-font-truetype" />
              </manifest>
              <spine><itemref idref="chapter-one" /></spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(
            package: package,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["META-INF/encryption.xml"] = Encryption(
                    "http://www.idpf.org/2008/embedding",
                    "EPUB/fonts/book.ttf"),
            },
            additionalBinaryEntries: new Dictionary<string, byte[]>
            {
                ["EPUB/fonts/book.ttf"] = [0x00, 0x01, 0x02],
            });
        workspace.Write("legacy-font-obfuscation.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateInventoryStatus.ReviewRequired, publication.Status);
        Assert.Equal(EpubPrivateInventoryProtectionStatus.FontObfuscationOnly, publication.Protection);
        Assert.Equal(1, publication.Resources.Fonts);
        Assert.Equal(0, publication.Resources.Other);
    }

    [Fact]
    public async Task Inventory_RecordsRightsMetadataAsReviewWithoutCallingItDrm()
    {
        using var workspace = new InventoryWorkspace();
        using var epub = MinimalEpubFactory.Create(additionalTextEntries: new Dictionary<string, string>
        {
            ["META-INF/rights.xml"] = "<rights xmlns=\"urn:example:rights\" />",
        });
        workspace.Write("rights.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateInventoryStatus.ReviewRequired, publication.Status);
        Assert.Equal(EpubPrivateInventoryProtectionStatus.RightsMetadataPresent, publication.Protection);
    }

    [Fact]
    public async Task Inventory_RejectsMaliciousProtectionXmlWithoutResolvingExternalEntity()
    {
        using var workspace = new InventoryWorkspace();
        using var epub = MinimalEpubFactory.Create(additionalTextEntries: new Dictionary<string, string>
        {
            ["META-INF/encryption.xml"] = """
                <?xml version="1.0"?>
                <!DOCTYPE encryption [<!ENTITY external SYSTEM "file:///private/secret">]>
                <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">&external;</encryption>
                """,
        });
        workspace.Write("malicious.epub", epub.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(workspace.Root);

        var publication = Assert.Single(report.Publications);
        Assert.Equal(EpubPrivateInventoryStatus.Protected, publication.Status);
        Assert.Contains(publication.Diagnostics, static item =>
            item.Code == EpubPrivateInventoryDiagnosticCodes.InvalidProtectionMetadata);
    }

    [Fact]
    public async Task Inventory_ReportsBoundedDiscoveryWithoutPersistingPaths()
    {
        using var workspace = new InventoryWorkspace();
        using var first = MinimalEpubFactory.Create();
        using var second = MinimalEpubFactory.Create(chapterOne: "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>Other</p></body></html>");
        workspace.Write("one.epub", first.ToArray());
        workspace.Write("two.epub", second.ToArray());

        var report = await new EpubPrivateInventoryService().InventoryAsync(
            workspace.Root,
            new EpubPrivateInventoryOptions(maximumCandidateFiles: 1));
        var json = Encoding.UTF8.GetString(EpubPrivateInventoryReportJsonSerializer.Serialize(report));

        Assert.Equal(1, report.Summary.DiscoveredFiles);
        Assert.Contains(report.Diagnostics, static item =>
            item.Code == EpubPrivateInventoryDiagnosticCodes.SearchLimitExceeded
            && item.Severity == EpubPrivateInventoryDiagnosticSeverity.Error);
        Assert.DoesNotContain(workspace.Root, json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(EpubPrivateInventoryReport.CurrentFormat, document.RootElement.GetProperty("format").GetString());
    }

    private static string Encryption(string algorithm, string path = "EPUB/fonts/book.otf") => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                    xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
          <enc:EncryptedData>
            <enc:EncryptionMethod Algorithm="{{algorithm}}" />
            <enc:CipherData><enc:CipherReference URI="{{path}}" /></enc:CipherData>
          </enc:EncryptedData>
        </encryption>
        """;

    private sealed class InventoryWorkspace : IDisposable
    {
        public InventoryWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(),
                $"flow-private-inventory-tests-{Guid.NewGuid():N}")).FullName;
        }

        public string Root { get; }

        public string Write(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
