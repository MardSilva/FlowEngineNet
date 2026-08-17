using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubTableOfContentsTests
{
    private const string Epub3Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:epub3-toc</dc:identifier>
            <dc:title>Navigation test</dc:title>
          </metadata>
          <manifest>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
            <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
          </manifest>
          <spine>
            <itemref idref="chapter-one" />
            <itemref idref="chapter-two" />
          </spine>
        </package>
        """;

    private const string ChapterWithEncodedAnchor = """
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>Opening</title></head>
          <body>
            <h1 id="start">Opening</h1>
            <h2 id="café">Café</h2>
            <p>Text.</p>
          </body>
        </html>
        """;

    [Fact]
    public async Task ImportAsync_Epub3Navigation_RoundTripsAndRendersEveryResolvedTarget()
    {
        const string navigation = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Navigation</title></head>
              <body>
                <nav epub:type="toc" id="toc">
                  <h1>Book contents</h1>
                  <ol>
                    <li><a href="text/chapter-1.xhtml#start"><strong>Opening</strong></a>
                      <ol><li><a href="text/chapter-1.xhtml#caf%C3%A9"><em>Café section</em></a></li></ol>
                    </li>
                    <li><a href="text/chapter-2.xhtml#end">Ending</a></li>
                  </ol>
                </nav>
              </body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: Epub3Package,
            chapterOne: ChapterWithEncodedAnchor,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string> { ["EPUB/nav.xhtml"] = navigation });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var imported = Assert.IsType<FlowDocument>(result.Document);
        var toc = Assert.IsType<TableOfContents>(imported.Content.Children[0]);
        Assert.Equal([1, 2, 1], toc.Entries.Select(static entry => entry.Level));
        Assert.IsType<Strong>(toc.Entries[0].Label.Single());
        Assert.IsType<Emphasis>(toc.Entries[1].Label.Single());
        Assert.All(toc.Entries, entry => Assert.True(imported.TryResolveAnchor(entry.Target, out _)));

        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(imported, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        var roundTrippedToc = Assert.IsType<TableOfContents>(roundTripped.Content.Children[0]);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var parsedHtml = XDocument.Parse(html);
        foreach (var entry in roundTrippedToc.Entries)
        {
            var expectedId = entry.Target.TargetId.Value;
            Assert.True(roundTripped.TryResolveAnchor(entry.Target, out var target));
            Assert.Equal(expectedId, target.Id.Value);
            Assert.Contains(parsedHtml.Descendants().Where(static element => element.Name.LocalName == "a"), anchor =>
                string.Equals((string?)anchor.Attribute("href"), $"#{expectedId}", StringComparison.Ordinal));
            Assert.Contains(parsedHtml.Descendants(), element =>
                string.Equals((string?)element.Attribute("id"), expectedId, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ImportAsync_UsesNcxForEpub2AndPreservesHierarchy()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:epub2-toc</dc:identifier>
                <dc:title>NCX test</dc:title>
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine toc="ncx"><itemref idref="chapter-one"/><itemref idref="chapter-two"/></spine>
            </package>
            """;
        const string ncx = """
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <docTitle><text>Contents</text></docTitle>
              <navMap>
                <navPoint id="one"><navLabel><text>Opening</text></navLabel><content src="text/chapter-1.xhtml#start"/>
                  <navPoint id="one-a"><navLabel><text>Ending</text></navLabel><content src="text/chapter-2.xhtml#end"/></navPoint>
                </navPoint>
              </navMap>
            </ncx>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: ChapterWithEncodedAnchor,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string> { ["EPUB/toc.ncx"] = ncx });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var toc = Assert.IsType<TableOfContents>(Assert.IsType<FlowDocument>(result.Document).Content.Children[0]);
        Assert.Equal([1, 2], toc.Entries.Select(static entry => entry.Level));
        Assert.Equal("Contents", Assert.IsType<Text>(toc.Title.Single()).Value);
    }

    [Fact]
    public async Task ImportAsync_PrefersNavigationDocumentAndDiagnosesMalformedOrConflictingEntries()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:toc-diagnostics</dc:identifier><dc:title>Diagnostics</dc:title>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine toc="ncx"><itemref idref="chapter-one"/><itemref idref="chapter-two"/></spine>
            </package>
            """;
        const string navigation = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><body>
              <nav epub:type="toc"><ol>
                <li><a href="text/chapter-1.xhtml#start">Navigation wins</a></li>
                <li><a href="text/chapter-1.xhtml#start"></a></li>
                <li><a href="text/missing.xhtml#lost">Missing</a></li>
                <li><a href="nav.xhtml#toc">Circular</a></li>
                <li id="deep-1"><a href="text/chapter-1.xhtml#start">1</a><ol>
                  <li id="deep-2"><a href="text/chapter-1.xhtml#start">2</a><ol>
                    <li id="deep-3"><a href="text/chapter-1.xhtml#start">3</a><ol>
                      <li id="deep-4"><a href="text/chapter-1.xhtml#start">4</a><ol>
                        <li id="deep-5"><a href="text/chapter-1.xhtml#start">5</a><ol>
                          <li id="deep-6"><a href="text/chapter-1.xhtml#start">6</a><ol>
                            <li id="deep-7"><a href="text/chapter-1.xhtml#start">7</a></li>
                          </ol></li>
                        </ol></li>
                      </ol></li>
                    </ol></li>
                  </ol></li>
                </ol></li>
              </ol></nav>
              <nav epub:type="toc"><ol><li><a href="text/chapter-2.xhtml#end">Second TOC</a></li></ol></nav>
            </body></html>
            """;
        const string ncx = """
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/"><navMap>
              <navPoint id="n1"><navLabel><text>Conflicting NCX</text></navLabel><content src="text/chapter-2.xhtml#end"/></navPoint>
            </navMap></ncx>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: ChapterWithEncodedAnchor,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/nav.xhtml"] = navigation,
                ["EPUB/toc.ncx"] = ncx,
            });

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        var toc = Assert.IsType<TableOfContents>(document.Content.Children[0]);
        Assert.Equal("Navigation wins", Assert.IsType<Text>(toc.Entries[0].Label.Single()).Value);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MultipleTableOfContents);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.EmptyTableOfContentsEntry);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MissingTableOfContentsTarget);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.CircularTableOfContentsReference);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidTableOfContentsLevel);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.TableOfContentsConflict);
        Assert.DoesNotContain(toc.Entries, static entry =>
            entry.Label.OfType<Text>().Any(static text => text.Value is "Missing" or "Circular"));
    }
}
