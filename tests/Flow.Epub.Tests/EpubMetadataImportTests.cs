using System.Text;
using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubMetadataImportTests
{
    private const string SimpleChapter = """
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter</title></head>
          <body><h1 id="start">Chapter</h1><p>Text.</p></body>
        </html>
        """;

    [Fact]
    public async Task ImportAsync_MapsCanonicalEpub3MetadataAndRetainsTypedSourceReport()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="primary-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="legacy-id">9780000000001</dc:identifier>
                <dc:identifier id="primary-id">urn:isbn:9780000000002</dc:identifier>
                <dc:title id="subtitle">A metadata experiment</dc:title>
                <dc:title id="main-title">The Flow Book</dc:title>
                <meta refines="#subtitle" property="title-type">subtitle</meta>
                <meta refines="#subtitle" property="display-seq">2</meta>
                <meta refines="#main-title" property="title-type">main</meta>
                <meta refines="#main-title" property="display-seq">1</meta>
                <dc:creator id="author-one">Ana Flow</dc:creator>
                <meta refines="#author-one" property="role" scheme="marc:relators">aut</meta>
                <dc:creator id="author-two">Bruno Flow</dc:creator>
                <meta refines="#author-two" property="role" scheme="marc:relators">aut</meta>
                <dc:creator id="editor">Editor Flow</dc:creator>
                <meta refines="#editor" property="role" scheme="marc:relators">edt</meta>
                <dc:contributor id="illustrator">Illustrator Flow</dc:contributor>
                <meta refines="#illustrator" property="role" scheme="marc:relators">ill</meta>
                <dc:publisher>Flow Press</dc:publisher>
                <dc:language>pt-PT</dc:language>
                <dc:language>en-US</dc:language>
                <dc:description>Semantic metadata.</dc:description>
                <dc:subject>Digital publishing</dc:subject>
                <dc:subject>Accessibility</dc:subject>
                <dc:date>2026-08-17</dc:date>
                <dc:rights>Copyright Flow contributors</dc:rights>
                <meta property="dcterms:modified">2026-08-17T10:20:30Z</meta>
                <meta property="schema:accessMode">textual</meta>
                <meta property="schema:accessModeSufficient">textual</meta>
                <meta property="schema:accessibilityFeature">tableOfContents</meta>
                <meta property="schema:accessibilityHazard">none</meta>
                <meta property="schema:accessibilitySummary">Structured headings and navigation.</meta>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" properties="cover-image" />
              </manifest>
              <spine><itemref idref="chapter-one"/><itemref idref="chapter-two"/></spine>
            </package>
            """;
        await using var epub = MinimalEpubFactory.Create(package: package, chapterOne: SimpleChapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("urn:isbn:9780000000002", document.Identity.Id.Value);
        Assert.Equal("The Flow Book", document.Metadata.Title);
        Assert.Equal("A metadata experiment", document.Metadata.Subtitle);
        Assert.Equal("pt-PT", document.Metadata.Language);
        Assert.Equal(["Ana Flow", "Bruno Flow"], document.Metadata.Authors.ToArray());
        Assert.Equal("Semantic metadata.", document.Metadata.Description);

        var report = Assert.IsType<EpubMetadataReport>(result.MetadataReport);
        Assert.Equal("primary-id", report.UniqueIdentifierId);
        Assert.Equal(2, report.Identifiers.Length);
        Assert.Equal(3, report.Creators.Length);
        Assert.Equal("edt", Assert.Single(report.Creators[2].Roles));
        Assert.Equal("ill", Assert.Single(Assert.Single(report.Contributors).Roles));
        Assert.Equal("Flow Press", Assert.Single(report.Publishers).Value);
        Assert.Equal(["pt-PT", "en-US"], report.Languages.Select(static item => item.Value));
        Assert.Equal(2, report.Subjects.Length);
        Assert.True(Assert.Single(report.Dates).IsValid);
        Assert.Equal("Copyright Flow contributors", Assert.Single(report.Rights).Value);
        Assert.Equal("2026-08-17T10:20:30Z", report.Modified);
        Assert.Equal("cover-image", Assert.IsType<EpubCoverMetadata>(report.Cover).ItemId);
        Assert.Equal("tableOfContents", Assert.Single(report.Accessibility.Features));
        Assert.Equal("Structured headings and navigation.", Assert.Single(report.Accessibility.Summaries));

        await using var json = new MemoryStream();
        await new FlowJsonDocumentSerializer().SerializeAsync(document, json);
        var serialized = Encoding.UTF8.GetString(json.ToArray());
        Assert.DoesNotContain("Flow Press", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("accessibilityFeature", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_ReadsEpub2RolesSchemesContributorsAndCoverMetadata()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:identifier id="book-id" opf:scheme="ISBN">9780000000003</dc:identifier>
                <dc:title>EPUB 2 metadata</dc:title>
                <dc:creator id="first" opf:role="aut" opf:file-as="Writer, One">Writer One</dc:creator>
                <dc:creator id="second" opf:role="aut">Writer Two</dc:creator>
                <dc:contributor id="translator" opf:role="trl">Translator</dc:contributor>
                <dc:publisher>Legacy Press</dc:publisher>
                <dc:language>en</dc:language>
                <meta name="cover" content="cover-image" />
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" />
              </manifest>
              <spine><itemref idref="chapter-one"/><itemref idref="chapter-two"/></spine>
            </package>
            """;
        await using var epub = MinimalEpubFactory.Create(package: package, chapterOne: SimpleChapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(["Writer One", "Writer Two"], Assert.IsType<FlowDocument>(result.Document).Metadata.Authors.ToArray());
        var report = Assert.IsType<EpubMetadataReport>(result.MetadataReport);
        Assert.Equal("ISBN", Assert.Single(report.Identifiers).Scheme);
        Assert.Equal("Writer, One", report.Creators[0].FileAs);
        Assert.Equal("trl", Assert.Single(Assert.Single(report.Contributors).Roles));
        Assert.Equal("epub2-meta-cover", Assert.IsType<EpubCoverMetadata>(report.Cover).Source);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesInvalidMissingOrConflictingMetadataWithoutLosingContent()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier> </dc:identifier>
                <dc:title id="main-one">First title</dc:title>
                <dc:title id="main-two">Second title</dc:title>
                <meta refines="#main-one" property="title-type">main</meta>
                <meta refines="#main-two" property="title-type">main</meta>
                <meta refines="#missing" property="role">aut</meta>
                <dc:language>en_US</dc:language>
                <dc:language>pt-BR</dc:language>
                <dc:date>not-a-date</dc:date>
                <meta property="dcterms:modified">not-a-date-time</meta>
                <meta name="cover" content="missing-cover" />
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine><itemref idref="chapter-one"/><itemref idref="chapter-two"/></spine>
            </package>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: SimpleChapter,
            includeImage: false);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("First title", document.Metadata.Title);
        Assert.Equal("pt-BR", document.Metadata.Language);
        Assert.StartsWith("urn:flow:epub:", document.Identity.Id.Value, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MissingIdentifier);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidLanguage);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidMetadata);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.OrphanMetadataRefinement);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MetadataConflict);
        Assert.Null(Assert.IsType<EpubMetadataReport>(result.MetadataReport).Cover);
    }

    [Fact]
    public void MetadataReport_CopiesInputCollections()
    {
        var values = new List<EpubMetadataValue> { new("Flow Press", "publisher", "en") };

        var report = new EpubMetadataReport(null, null, publishers: values);
        values[0] = new EpubMetadataValue("Changed", null, null);
        values.Add(new EpubMetadataValue("Added", null, null));

        Assert.Equal("Flow Press", Assert.Single(report.Publishers).Value);
    }
}
