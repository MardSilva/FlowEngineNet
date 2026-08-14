using System.Security.Cryptography;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Security.Tests;

public sealed class CanonicalizationTests
{
    private readonly FlowDocumentCanonicalizer _canonicalizer = new();

    [Fact]
    public void Canonicalize_SameDocumentAlwaysProducesSameBytes()
    {
        var document = CreateDocument("Canonical content");

        var first = _canonicalizer.Canonicalize(document);
        var second = _canonicalizer.Canonicalize(document);

        Assert.Equal(first, second);
        Assert.Equal(FlowDocumentCanonicalizer.Version, _canonicalizer.CanonicalizationVersion);
    }

    [Fact]
    public void Canonicalize_SortsAssetsByStableId()
    {
        var first = CreateDocument(
            "Assets",
            assets:
            [
                Asset("z.bin", [3]),
                Asset("a.bin", [1]),
            ]);
        var second = CreateDocument(
            "Assets",
            assets:
            [
                Asset("a.bin", [1]),
                Asset("z.bin", [3]),
            ]);

        Assert.Equal(_canonicalizer.Canonicalize(first), _canonicalizer.Canonicalize(second));
    }

    [Fact]
    public void ComputeHash_ContentChangeChangesHash()
    {
        var service = CreateIntegrityService();

        var original = service.ComputeHash(CreateDocument("The amount is €1,000."));
        var changed = service.ComputeHash(CreateDocument("The amount is €10,000."));

        Assert.NotEqual(original.Hash, changed.Hash);
    }

    [Fact]
    public void ComputeHash_CanonicalMetadataAndAssetBytesChangeHash()
    {
        var service = CreateIntegrityService();
        var original = CreateDocument("Content", title: "Original", assets: [Asset("asset.bin", [1])]);
        var metadataChanged = CreateDocument("Content", title: "Changed", assets: [Asset("asset.bin", [1])]);
        var assetChanged = CreateDocument("Content", title: "Original", assets: [Asset("asset.bin", [2])]);

        var originalHash = service.ComputeHash(original).Hash;

        Assert.NotEqual(originalHash, service.ComputeHash(metadataChanged).Hash);
        Assert.NotEqual(originalHash, service.ComputeHash(assetChanged).Hash);
    }

    [Fact]
    public void ComputeHash_PresentationThemeAndExistingIntegrityDoNotChangeHash()
    {
        var firstPresentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Georgia", fontSize: Length.Px(16))),
            ]),
            theme: ReadingTheme.Light);
        var secondPresentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Arial", fontSize: Length.Px(28))),
            ]),
            theme: ReadingTheme.Dark);
        var first = CreateDocument(
            "Content",
            firstPresentation,
            integrity: new DocumentIntegrity("SHA-256", "OLD", "old-c14n"));
        var second = CreateDocument(
            "Content",
            secondPresentation,
            integrity: new DocumentIntegrity("SHA-256", "OTHER", "other-c14n"));

        var service = CreateIntegrityService();

        Assert.Equal(service.ComputeHash(first), service.ComputeHash(second));
    }

    [Fact]
    public void ComputeHash_UserPreferencesAndResolvedStylesRemainOutsideDocumentHash()
    {
        var document = CreateDocument("Reader state");
        var service = CreateIntegrityService();
        var before = service.ComputeHash(document);

        var resolved = new TypographyResolver().Resolve(
            document,
            new UserReadingPreferences(
                preferredBodyFont: "Reader Serif",
                fontScale: 1.8,
                theme: ReadingTheme.Sepia));
        var after = service.ComputeHash(document);

        Assert.Equal(before, after);
        Assert.Equal("Reader Serif", resolved.Typography[TypographyRole.Body].FontFamily);
        Assert.Null(document.Presentation);
    }

    [Fact]
    public void ComputeHash_UsesStandardSha256AndReportsProfile()
    {
        var document = CreateDocument("Hash profile");
        var canonicalBytes = _canonicalizer.Canonicalize(document);

        var result = CreateIntegrityService().ComputeHash(document);

        Assert.Equal("SHA-256", result.Algorithm);
        Assert.Equal(64, result.Hash.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(canonicalBytes)), result.Hash);
        Assert.Equal("flow-c14n-0.1", result.CanonicalizationVersion);
    }

    private Sha256DocumentIntegrityService CreateIntegrityService() => new(_canonicalizer);

    private static FlowDocument CreateDocument(
        string text,
        DocumentPresentation? presentation = null,
        string title = "Canonical document",
        IEnumerable<FlowAsset>? assets = null,
        DocumentIntegrity? integrity = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:document:canonicalization-tests"), "1"),
            new DocumentMetadata(title, "en", ["Flow contributors"]),
            new DocumentContent(
            [
                new Chapter(
                    new NodeId("chapter-one"),
                    [new Paragraph(new NodeId("p-one"), [new Text(text)])]),
            ]),
            assets,
            presentation,
            integrity);

    private static FlowAsset Asset(string id, byte[] bytes) =>
        new(new AssetId(id), "application/octet-stream", id, bytes);
}
