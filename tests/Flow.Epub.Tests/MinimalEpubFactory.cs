using System.IO.Compression;
using System.Text;

namespace Flow.Epub.Tests;

internal static class MinimalEpubFactory
{
    private const string DefaultContainer = """
        <?xml version="1.0" encoding="utf-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" />
          </rootfiles>
        </container>
        """;

    private const string DefaultPackage = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:isbn:9780000000001</dc:identifier>
            <dc:title>EPUB mínimo do Flow</dc:title>
            <dc:language>pt-PT</dc:language>
            <dc:creator>Flow contributors</dc:creator>
            <dc:description>Fixture gerada pelo próprio projeto.</dc:description>
          </metadata>
          <manifest>
            <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
            <item id="cover-image" href="images/flow.png" media-type="image/png" />
          </manifest>
          <spine>
            <itemref idref="chapter-one" />
            <itemref idref="chapter-two" />
          </spine>
        </package>
        """;

    private const string ChapterOne = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" lang="pt-PT">
          <head><title>Começo</title></head>
          <body>
            <h1 id="start">Começo</h1>
            <p id="intro">Um <strong>documento</strong> que aponta para o <a href="chapter-2.xhtml#end">fim</a>.</p>
            <ul id="ideas"><li>Conteúdo</li><li>Estrutura</li></ul>
            <figure id="diagram">
              <img src="../images/flow.png" alt="Um pixel do Flow" />
              <figcaption>Figura mínima</figcaption>
            </figure>
          </body>
        </html>
        """;

    private const string ChapterTwo = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" lang="pt-PT">
          <head><title>Fim</title></head>
          <body>
            <h1 id="end">Fim</h1>
            <p>O segundo item do spine vem depois do primeiro.</p>
            <ol start="2"><li>Segundo</li><li>Terceiro</li></ol>
          </body>
        </html>
        """;

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    internal static MemoryStream Create(
        string? container = null,
        string? package = null,
        string? chapterOne = null,
        bool includeSecondChapter = true,
        bool includeImage = true,
        IReadOnlyDictionary<string, string>? additionalTextEntries = null)
    {
        var result = new MemoryStream();
        using (var archive = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            AddText(archive, "META-INF/container.xml", container ?? DefaultContainer);
            AddText(archive, "EPUB/package.opf", package ?? DefaultPackage);
            AddText(archive, "EPUB/text/chapter-1.xhtml", chapterOne ?? ChapterOne);
            if (includeSecondChapter)
            {
                AddText(archive, "EPUB/text/chapter-2.xhtml", ChapterTwo);
            }

            if (includeImage)
            {
                var image = archive.CreateEntry("EPUB/images/flow.png", CompressionLevel.Optimal);
                using var stream = image.Open();
                stream.Write(OnePixelPng);
            }

            if (additionalTextEntries is not null)
            {
                foreach (var (path, content) in additionalTextEntries)
                {
                    AddText(archive, path, content);
                }
            }
        }

        result.Position = 0;
        return result;
    }

    private static void AddText(
        ZipArchive archive,
        string path,
        string content,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(path, compressionLevel);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
