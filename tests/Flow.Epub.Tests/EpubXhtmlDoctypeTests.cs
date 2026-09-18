using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubXhtmlDoctypeTests
{
    public static TheoryData<string> SupportedDoctypes => new()
    {
        "<!DOCTYPE html>",
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">",
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">",
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Frameset//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-frameset.dtd\">",
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">",
    };

    [Theory]
    [MemberData(nameof(SupportedDoctypes))]
    public async Task ImportAsync_AcceptsKnownXhtmlDoctypeWithoutLoadingTheDtd(string doctype)
    {
        var chapter = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            {{doctype}}
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body><h1 id="start">Capítulo seguro</h1><p>Texto &amp; conteúdo preservado.</p></body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.NotNull(result.Document);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml);
        Assert.Contains(
            result.Document.Content.Children.SelectMany(DescendantsAndSelf).OfType<Paragraph>(),
            static paragraph => paragraph.Content.OfType<Text>().Any(text =>
                text.Value.Contains("Texto & conteúdo preservado.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ImportAsync_RejectsUnknownPublicXhtmlDoctype()
    {
        const string chapter = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE html PUBLIC "-//Example//DTD XHTML Custom//EN" "https://example.invalid/custom.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>Não importar.</p></body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml
            && diagnostic.Severity == EpubDiagnosticSeverity.Error
            && diagnostic.Message.Contains("unsupported or unsafe DOCTYPE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_RejectsInternalSubsetEvenForKnownPublicDoctype()
    {
        const string chapter = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" [
              <!ENTITY xxe SYSTEM "file:///not-allowed">
            ]>
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>&xxe;</p></body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml
            && diagnostic.Severity == EpubDiagnosticSeverity.Error
            && diagnostic.Message.Contains("unsupported or unsafe DOCTYPE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_AcceptsKnownNcxDoctypeWithoutLoadingTheDtd()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:test:ncx-doctype</dc:identifier>
                <dc:title>NCX seguro</dc:title>
                <dc:language>pt-BR</dc:language>
              </metadata>
              <manifest>
                <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
              </manifest>
              <spine toc="ncx"><itemref idref="chapter" /></spine>
            </package>
            """;
        const string ncx = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE ncx PUBLIC "-//NISO//DTD ncx 2005-1//EN" "http://www.daisy.org/z3986/2005/ncx-2005-1.dtd">
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head />
              <docTitle><text>NCX seguro</text></docTitle>
              <navMap><navPoint id="start" playOrder="1"><navLabel><text>Começo</text></navLabel><content src="text/chapter-1.xhtml#start" /></navPoint></navMap>
            </ncx>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            includeSecondChapter: false,
            additionalTextEntries: new Dictionary<string, string> { ["EPUB/toc.ncx"] = ncx });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.NotNull(result.Document);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml);
        Assert.IsType<TableOfContents>(result.Document.Content.Children[0]);
    }

    [Fact]
    public async Task ImportAsync_StillRejectsDoctypeInContainerXml()
    {
        const string container = """
            <?xml version="1.0"?>
            <!DOCTYPE container PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="EPUB/package.opf" /></rootfiles>
            </container>
            """;
        await using var epub = MinimalEpubFactory.Create(container: container);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml
            && diagnostic.Severity == EpubDiagnosticSeverity.Error);
    }

    private static IEnumerable<DocumentNode> DescendantsAndSelf(DocumentNode node)
    {
        yield return node;
        if (node is not BlockContainerNode container)
        {
            yield break;
        }

        foreach (var child in container.Children.SelectMany(DescendantsAndSelf))
        {
            yield return child;
        }
    }
}
