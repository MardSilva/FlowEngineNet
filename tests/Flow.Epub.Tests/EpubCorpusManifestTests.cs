using System.Security.Cryptography;
using System.Text;
using Flow.Epub;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubCorpusManifestTests
{
    private readonly EpubCorpusManifestJsonSerializer serializer = new();

    [Fact]
    public void Read_ValidManifestCreatesImmutableTypedContract()
    {
        using var source = OpenPublicManifest();

        var result = serializer.Read(source);

        Assert.True(result.IsSuccess, JoinDiagnostics(result));
        var manifest = Assert.IsType<EpubCorpusManifest>(result.Manifest);
        Assert.Equal(EpubCorpusManifest.CurrentFormat, manifest.Format);
        Assert.Equal(3, manifest.Publications.Length);
        var publication = manifest.Publications.Single(static item => item.Id.Value == "flow-minimal-epub3");
        Assert.Equal("flow-minimal-epub3", publication.Id.Value);
        Assert.Equal("EPUB mínimo do Flow", publication.Title);
        Assert.Equal(EpubCorpusPublicationKind.ProjectFixture, publication.Kind);
        Assert.Equal(EpubCorpusRedistribution.Allowed, publication.Redistribution);
        Assert.Equal(EpubVersionFamily.Epub3, publication.ExpectedEpubVersion);
        Assert.Equal("pt-PT", Assert.Single(publication.Languages));
        Assert.Equal(publication.ExpectedFeatures.Order(StringComparer.Ordinal), publication.ExpectedFeatures);
    }

    [Fact]
    public void Contracts_CopyMutableInputCollections()
    {
        var languages = new List<string> { "en" };
        var features = new List<string> { "spine" };
        var publication = new EpubCorpusPublication(
            new EpubCorpusPublicationId("immutable"),
            "Immutable input",
            "project:immutable",
            new EpubCorpusLicense("Project fixture", "Project source"),
            EpubCorpusPublicationKind.ProjectFixture,
            EpubCorpusRedistribution.Allowed,
            "fixtures/immutable.epub",
            EpubVersionFamily.Epub3,
            42,
            languages,
            ["xhtml"],
            features,
            ["valid"],
            [],
            new EpubCorpusSha256(new string('A', 64)));

        languages.Add("pt-BR");
        features.Add("table");

        Assert.Equal("en", Assert.Single(publication.Languages));
        Assert.Equal("spine", Assert.Single(publication.ExpectedFeatures));
    }

    [Fact]
    public void Read_PropertyAndEntryOrderDoNotChangeWrittenBytes()
    {
        const string reordered = """
            {
              "publications": [
                {
                  "sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                  "knownLimitations": ["z-limit", "a-limit"],
                  "expectedResults": ["valid", "imported"],
                  "expectedFeatures": ["table", "spine"],
                  "languages": ["en"],
                  "expectedSizeBytes": 20,
                  "expectedEpubVersion": "epub2",
                  "relativePath": "fixtures/b.epub",
                  "redistribution": "allowed",
                  "kind": "projectFixture",
                  "license": { "evidence": "Project source", "name": "Project fixture" },
                  "origin": "project:b",
                  "title": "Book B",
                  "id": "book-b",
                  "expectedResources": ["xhtml"]
                },
                {
                  "title": "Book A",
                  "id": "book-a",
                  "origin": "project:a",
                  "license": { "name": "Project fixture", "evidence": "Project source" },
                  "kind": "projectFixture",
                  "redistribution": "allowed",
                  "relativePath": "fixtures/a.epub",
                  "expectedEpubVersion": "epub3",
                  "expectedSizeBytes": 10,
                  "languages": ["pt-BR"],
                  "expectedResources": ["xhtml"],
                  "expectedFeatures": ["spine"],
                  "expectedResults": ["valid"],
                  "knownLimitations": [],
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
                }
              ],
              "format": "flow-epub-corpus-0.1"
            }
            """;
        var first = Read(reordered);
        var second = new EpubCorpusManifest(
            EpubCorpusManifest.CurrentFormat,
            first.Publications.Reverse());

        var firstBytes = Write(first);
        var secondBytes = Write(second);

        Assert.Equal(firstBytes, secondBytes);
        var text = Encoding.UTF8.GetString(firstBytes);
        Assert.True(text.IndexOf("book-a", StringComparison.Ordinal) < text.IndexOf("book-b", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', firstBytes[^1]);
        Assert.False(firstBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Read_DuplicateIdProducesStableDiagnostic()
    {
        var json = ValidJson().Replace(
            "]\n}",
            ",\n" + ValidPublicationJson("same-id", "fixtures/second.epub", new string('B', 64)) + "\n  ]\n}",
            StringComparison.Ordinal).Replace("\"sample-id\"", "\"same-id\"", StringComparison.Ordinal);

        var result = ReadResult(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Code == EpubCorpusDiagnosticCodes.DuplicatePublicationId);
        Assert.Equal("publications/same-id", diagnostic.Resource);
    }

    [Fact]
    public void Read_InvalidSha256ProducesComprehensibleDiagnostic()
    {
        var result = ReadResult(ValidJson().Replace(new string('A', 64), "not-a-hash", StringComparison.Ordinal));

        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubCorpusDiagnosticCodes.InvalidSha256
            && item.Resource == "$.publications[0].sha256");
    }

    [Theory]
    [InlineData("../book.epub")]
    [InlineData("C:/books/book.epub")]
    [InlineData("fixtures/./book.epub")]
    [InlineData("fixtures/%2E%2E/book.epub")]
    [InlineData("fixtures\\book.epub")]
    public void Read_UnsafePathProducesComprehensibleDiagnostic(string path)
    {
        var result = ReadResult(ValidJson().Replace("fixtures/sample.epub", path.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal));

        Assert.Contains(result.Diagnostics, static item => item.Code == EpubCorpusDiagnosticCodes.UnsafeRelativePath);
    }

    [Fact]
    public void Read_MissingLicenseProducesComprehensibleDiagnostic()
    {
        var json = ValidJson().Replace(
            "      \"license\": { \"name\": \"Project fixture\", \"evidence\": \"Project source\" },\n",
            string.Empty,
            StringComparison.Ordinal);

        var result = ReadResult(json);

        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubCorpusDiagnosticCodes.MissingRequiredValue
            && item.Resource == "$.publications[0].license");
    }

    [Fact]
    public void Read_UnknownVersionAndInvalidEncodingAreRejected()
    {
        var invalidVersion = ReadResult(ValidJson().Replace("epub3", "epub4", StringComparison.Ordinal));
        Assert.Contains(invalidVersion.Diagnostics, static item => item.Code == EpubCorpusDiagnosticCodes.UnsupportedEpubVersion);

        var crlfWithBom = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(ValidJson().Replace("\n", "\r\n", StringComparison.Ordinal)))
            .ToArray();
        using var stream = new MemoryStream(crlfWithBom);
        var invalidEncoding = serializer.Read(stream);
        Assert.Contains(invalidEncoding.Diagnostics, static item => item.Code == EpubCorpusDiagnosticCodes.InvalidEncoding);
    }

    [Fact]
    public void Read_UnknownManifestFormatIsRejected()
    {
        var result = ReadResult(ValidJson().Replace("flow-epub-corpus-0.1", "flow-epub-corpus-9.9", StringComparison.Ordinal));

        Assert.Contains(result.Diagnostics, static item => item.Code == EpubCorpusDiagnosticCodes.UnsupportedFormat);
    }

    [Fact]
    public async Task CorpusMetadataDoesNotChangeCanonicalFlowHash()
    {
        await using var epub = MinimalEpubFactory.Create();
        var document = Assert.IsType<Flow.Documents.FlowDocument>((await new EpubImporter().ImportAsync(epub)).Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var before = integrity.ComputeHash(document);

        using var manifestSource = OpenPublicManifest();
        var manifest = Assert.IsType<EpubCorpusManifest>(serializer.Read(manifestSource).Manifest);
        _ = Write(manifest);

        Assert.Equal(before, integrity.ComputeHash(document));
    }

    [Fact]
    public void PublicManifestDescribesTheDeterministicProjectFixture()
    {
        using var source = OpenPublicManifest();
        var publications = Assert.IsType<EpubCorpusManifest>(serializer.Read(source).Manifest).Publications;
        var fixtures = EpubCorpusFixtureFactory.CreateAll();

        Assert.Equal(fixtures.Keys.Order(StringComparer.Ordinal), publications.Select(static item => item.Id.Value));
        foreach (var publication in publications)
        {
            var bytes = fixtures[publication.Id.Value];
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.True(
                bytes.LongLength == publication.ExpectedSizeBytes && actualHash == publication.Sha256.Value,
                $"{publication.Id.Value}: size={bytes.LongLength}; sha256={actualHash}");
        }
    }

    [Fact]
    public void PublicManifestAlreadyUsesTheDeterministicRepresentation()
    {
        using var source = OpenPublicManifest();
        using var original = new MemoryStream();
        source.CopyTo(original);
        original.Position = 0;
        var manifest = Assert.IsType<EpubCorpusManifest>(serializer.Read(original).Manifest);

        Assert.Equal(original.ToArray(), Write(manifest));
    }

    private EpubCorpusManifest Read(string json)
    {
        var result = ReadResult(json);
        Assert.True(result.IsSuccess, JoinDiagnostics(result));
        return Assert.IsType<EpubCorpusManifest>(result.Manifest);
    }

    private EpubCorpusManifestReadResult ReadResult(string json)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return serializer.Read(source);
    }

    private byte[] Write(EpubCorpusManifest manifest)
    {
        using var destination = new MemoryStream();
        serializer.Write(manifest, destination);
        return destination.ToArray();
    }

    private static FileStream OpenPublicManifest() => File.OpenRead(
        Path.Combine(AppContext.BaseDirectory, "Corpus", "epub-corpus.json"));

    private static string JoinDiagnostics(EpubCorpusManifestReadResult result) => string.Join(
        Environment.NewLine,
        result.Diagnostics.Select(static item => $"{item.Code} {item.Resource}: {item.Message}"));

    private static string ValidJson() => $$"""
        {
          "format": "flow-epub-corpus-0.1",
          "publications": [
        {{ValidPublicationJson("sample-id", "fixtures/sample.epub", new string('A', 64))}}
          ]
        }
        """;

    private static string ValidPublicationJson(string id, string path, string hash) => $$"""
            {
              "id": "{{id}}",
              "title": "Sample title",
              "origin": "project:sample",
              "license": { "name": "Project fixture", "evidence": "Project source" },
              "kind": "projectFixture",
              "redistribution": "allowed",
              "relativePath": "{{path}}",
              "expectedEpubVersion": "epub3",
              "expectedSizeBytes": 42,
              "languages": ["en"],
              "expectedResources": ["xhtml"],
              "expectedFeatures": ["spine"],
              "expectedResults": ["valid"],
              "knownLimitations": [],
              "sha256": "{{hash}}"
            }
        """;
}
