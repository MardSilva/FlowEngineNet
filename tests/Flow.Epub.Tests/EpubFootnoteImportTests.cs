using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubFootnoteImportTests
{
    private const string Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="id">urn:test:notes</dc:identifier>
            <dc:title>Notes</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="notes" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
          </manifest>
          <spine><itemref idref="chapter" /><itemref idref="notes" /></spine>
        </package>
        """;

    [Fact]
    public async Task ImportAsync_MapsCrossDocumentEndnoteMultipleReferencesBacklinkAndRenderedLabel()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body><h1>Chapter</h1><p>
                First<a id="ref-one" epub:type="noteref" href="chapter-2.xhtml#note%2Done"><strong>1</strong></a>
                and second<a id="ref-two" role="doc-noteref" href="chapter-2.xhtml#note-one">1b</a>.
              </p></body>
            </html>
            """;
        const string notes = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body><h1>Notes</h1><ol><li id="note-one" epub:type="endnote">
                <p>Complete note. <a epub:type="backlink" href="chapter-1.xhtml#ref-one">back</a></p>
              </li></ol></body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: Package,
            chapterOne: chapter,
            chapterTwo: notes,
            includeImage: false);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var note = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Footnote>());
        var references = document.Index.Locations.Select(static item => item.Node)
            .OfType<Paragraph>()
            .SelectMany(static paragraph => paragraph.Content)
            .OfType<FootnoteReference>()
            .ToArray();
        Assert.Equal(2, references.Length);
        Assert.All(references, reference => Assert.Equal(note.Id, reference.TargetId));
        Assert.IsType<Strong>(references[0].Label.Single());
        Assert.Equal("1b", Assert.IsType<Text>(references[1].Label.Single()).Value);

        var backlink = note.Children.OfType<Paragraph>().SelectMany(static paragraph => paragraph.Content).OfType<Link>().Single();
        Assert.StartsWith("flow:", backlink.Target, StringComparison.Ordinal);
        Assert.True(document.TryResolveAnchor(Flow.Core.DocumentAnchor.Parse(backlink.Target), out _));

        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var parsed = XDocument.Parse(html);

        var renderedReferences = parsed.Descendants("a")
            .Where(element => (string?)element.Attribute("role") == "doc-noteref")
            .ToArray();
        Assert.Equal(2, renderedReferences.Length);
        Assert.Equal($"#{note.Id.Value}", (string?)renderedReferences[0].Attribute("href"));
        Assert.Equal("1", renderedReferences[0].Element("sup")?.Element("strong")?.Value);
        var renderedNote = Assert.Single(parsed.Descendants("section"), element =>
            (string?)element.Attribute("role") == "doc-footnote"
            && (string?)element.Attribute("id") == note.Id.Value);
        var renderedBacklink = Assert.Single(renderedNote.Descendants("a"));
        var backlinkTarget = ((string?)renderedBacklink.Attribute("href"))?[1..];
        Assert.NotNull(backlinkTarget);
        Assert.Single(parsed.Descendants(), element => (string?)element.Attribute("id") == backlinkTarget);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesOrphanReferenceUnreferencedNoteAndMissingBacklink()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body>
                <p>Broken<a epub:type="noteref" href="#missing">x</a></p>
                <aside id="unused" role="doc-footnote"><p>Unused <a role="doc-backlink" href="#missing-ref">back</a></p></aside>
              </body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: Package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.OrphanNoteReference);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.UnreferencedNote);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.MissingNoteBacklink);
        Assert.DoesNotContain(
            result.Document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>().SelectMany(static item => item.Content),
            static inline => inline is FootnoteReference);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesAmbiguousDestinationsAndNoteCycles()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body>
                <p><a epub:type="noteref" href="#duplicate">ambiguous</a></p>
                <aside id="duplicate" epub:type="footnote"><p>Duplicate one</p></aside>
                <aside id="duplicate" epub:type="footnote"><p>Duplicate</p></aside>
                <aside id="note-a" epub:type="footnote"><p>A <a epub:type="noteref" href="#note-b">2</a></p></aside>
                <aside id="note-b" epub:type="footnote"><p>B <a epub:type="noteref" href="#note-a">1</a></p></aside>
              </body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: Package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.AmbiguousNoteDestination);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.CircularNoteReference);
    }
}
