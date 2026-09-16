using System.Collections.Immutable;

namespace Flow.Epub.Tests;

internal static class EpubCorpusFixtureFactory
{
    internal static ImmutableDictionary<string, byte[]> CreateAll() => new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["flow-epub2-ncx"] = CreateEpub2Ncx(),
        ["flow-epub3-varied"] = CreateEpub3Varied(),
        ["flow-minimal-epub3"] = CreateBytes(MinimalEpubFactory.Create()),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static byte[] CreateEpub2Ncx()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:fixture:epub2-ncx</dc:identifier>
                <dc:title>Fixture EPUB 2 com NCX</dc:title>
                <dc:language>pt-BR</dc:language>
              </metadata>
              <manifest>
                <item id="one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
              </manifest>
              <spine toc="ncx">
                <itemref idref="one" />
                <itemref idref="two" />
              </spine>
            </package>
            """;
        const string chapterOne = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Primeiro</title></head><body>
              <h1 id="primeiro">Primeiro capítulo</h1>
              <p>Leitura inicial com <a href="chapter-2.xhtml#segundo">destino interno</a>.</p>
            </body></html>
            """;
        const string chapterTwo = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Segundo</title></head><body>
              <h1 id="segundo">Segundo capítulo</h1><p>Fim da fixture EPUB 2.</p>
            </body></html>
            """;
        const string ncx = """
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head><meta name="dtb:uid" content="urn:flow:fixture:epub2-ncx" /></head>
              <docTitle><text>Fixture EPUB 2 com NCX</text></docTitle>
              <navMap>
                <navPoint id="nav-1" playOrder="1"><navLabel><text>Primeiro</text></navLabel><content src="text/chapter-1.xhtml#primeiro" /></navPoint>
                <navPoint id="nav-2" playOrder="2"><navLabel><text>Segundo</text></navLabel><content src="text/chapter-2.xhtml#segundo" /></navPoint>
              </navMap>
            </ncx>
            """;
        return CreateBytes(MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapterOne,
            chapterTwo: chapterTwo,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EPUB/toc.ncx"] = ncx,
            }));
    }

    private static byte[] CreateEpub3Varied()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:fixture:epub3-varied</dc:identifier>
                <dc:title>Corpus variado: edição São Paulo</dc:title>
                <dc:language>pt-BR</dc:language><dc:language>ar</dc:language><dc:language>ja</dc:language>
                <meta property="dcterms:modified">2026-01-01T00:00:00Z</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="cover-page" href="cover.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter" href="text/cap%C3%ADtulo.xhtml" media-type="application/xhtml+xml" />
                <item id="notes" href="text/notes.xhtml" media-type="application/xhtml+xml" />
                <item id="css" href="styles/book.css" media-type="text/css" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" properties="cover-image" />
                <item id="safe-svg" href="images/safe.svg" media-type="image/svg+xml" />
                <item id="unknown" href="data/unknown.bin" media-type="application/octet-stream" />
              </manifest>
              <spine>
                <itemref idref="cover-page" linear="no" />
                <itemref idref="chapter" />
                <itemref idref="notes" />
              </spine>
            </package>
            """;
        const string navigation = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Sumário</title></head><body>
              <nav epub:type="toc"><h1>Sumário</h1><ol>
                <li><a href="text/cap%C3%ADtulo.xhtml#in%C3%ADcio">Capítulo com acento</a></li>
              </ol></nav>
            </body></html>
            """;
        const string cover = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:svg="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><head><title>Capa</title></head><body>
              <svg:svg role="img"><svg:title>Capa do corpus</svg:title><svg:desc>Marca do Flow</svg:desc><svg:image xlink:href="images/flow.png" /></svg:svg>
            </body></html>
            """;
        const string chapter = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xmlns:svg="http://www.w3.org/2000/svg" xmlns:m="http://www.w3.org/1998/Math/MathML" lang="pt-BR">
              <head><title>Capítulo variado</title><link rel="stylesheet" href="../styles/book.css" /><style>h1 { text-transform: uppercase; }</style></head><body>
                <main><section><h1 id="início">Isto é conteúdo em português</h1>
                  <p id="texto" style="text-align: justify">Texto com <strong>ênfase</strong> e nota <a epub:type="noteref" href="notes.xhtml#nota-1">[1]</a>.</p>
                  <p lang="ar" dir="rtl"><bdi>مرحبا</bdi> <bdo dir="ltr">Flow</bdo></p>
                  <p lang="ja"><ruby>漢<rt>かん</rt><rp>(</rp><rp>)</rp></ruby></p>
                  <figure><img src="../images/safe.svg" alt="Diagrama vetorial seguro" /><figcaption>Figura SVG</figcaption></figure>
                  <table><caption>Dados</caption><thead><tr><th id="col" scope="col">Nome</th></tr></thead><tbody><tr><td headers="col">Flow</td></tr></tbody></table>
                  <m:math alttext="x mais um"><m:mrow><m:mi>x</m:mi><m:mo>+</m:mo><m:mn>1</m:mn><m:script>alert(1)</m:script></m:mrow></m:math>
                  <svg:svg aria-label="vetor inline"><svg:rect width="10" height="10" onclick="alert(1)" /><svg:script>alert(1)</svg:script></svg:svg>
                  <video>Fallback textual de recurso desconhecido.</video>
                </section></main>
              </body></html>
            """;
        const string notes = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Notas</title></head><body>
              <aside id="nota-1" epub:type="footnote"><p>Nota em outro XHTML. <a href="cap%C3%ADtulo.xhtml#texto">Voltar</a></p></aside>
            </body></html>
            """;
        const string css = """
            body { font-family: serif; font-size: 1em; line-height: 1.5; }
            p { margin-bottom: 1em; text-indent: 1.2em; }
            #texto { letter-spacing: 0.02em; }
            .unsupported { position: absolute; float: left; }
            """;
        const string safeSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><title>Quadrado seguro</title><rect width="10" height="10" fill="blue" /></svg>
            """;

        return CreateBytes(MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            additionalTextEntries: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["EPUB/cover.xhtml"] = cover,
                ["EPUB/images/safe.svg"] = safeSvg,
                ["EPUB/nav.xhtml"] = navigation,
                ["EPUB/styles/book.css"] = css,
                ["EPUB/text/capítulo.xhtml"] = chapter,
                ["EPUB/text/notes.xhtml"] = notes,
            },
            additionalBinaryEntries: new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["EPUB/data/unknown.bin"] = [0x46, 0x4C, 0x4F, 0x57],
            }));
    }

    private static byte[] CreateBytes(MemoryStream stream)
    {
        using (stream)
        {
            return stream.ToArray();
        }
    }
}
