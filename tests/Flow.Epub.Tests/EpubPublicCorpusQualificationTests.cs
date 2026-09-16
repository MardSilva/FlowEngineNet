using System.Security.Cryptography;
using System.Text;
using Flow.Epub;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubPublicCorpusQualificationTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Coverage =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["flow-epub2-ncx"] = ["epub2", "ncx", "multiple-chapters", "reading-order"],
            ["flow-epub3-varied"] =
            [
                "epub3", "navigation-document", "portuguese-accents", "percent-encoded-path",
                "non-linear-spine", "xhtml-svg-raster-cover", "typed-css", "cross-xhtml-notes",
                "tables", "safe-svg", "malicious-svg", "safe-mathml", "malicious-mathml",
                "bidi", "mixed-languages", "ruby", "unsupported-resource-diagnostic",
            ],
            ["flow-minimal-epub3"] = ["epub3", "figures", "lists", "internal-links"],
        };

    [Fact]
    public void PublicCoverageMatrixNamesEveryRequiredPromptArea()
    {
        var covered = Coverage.Values.SelectMany(static item => item).ToHashSet(StringComparer.Ordinal);
        string[] required =
        [
            "epub2", "ncx", "epub3", "navigation-document", "portuguese-accents",
            "percent-encoded-path", "multiple-chapters", "non-linear-spine", "xhtml-svg-raster-cover",
            "typed-css", "cross-xhtml-notes", "tables", "safe-svg", "malicious-svg", "safe-mathml",
            "malicious-mathml", "bidi", "mixed-languages", "ruby", "unsupported-resource-diagnostic",
        ];

        Assert.All(required, item => Assert.Contains(item, covered));
    }

    [Fact]
    public async Task PublicCorpus_IsOfflineDeterministicAndMatchesReviewedBaseline()
    {
        using var workspace = new PublicCorpusWorkspace();
        var manifest = workspace.LoadAndMaterializeManifest();
        var accepted = EpubCorpusBaselineJsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Corpus", "epub-corpus-baseline.json")));

        var qualification = await new EpubCorpusQualificationService().QualifyAsync(
            manifest,
            new EpubCorpusDiscoveryOptions(workspace.Root),
            accepted);

        Assert.True(qualification.IsDeterministic);
        Assert.Equal(manifest.Publications.Length, qualification.Report.Summary.Total);
        var executionJson = Encoding.UTF8.GetString(EpubCorpusExecutionReportJsonSerializer.Serialize(qualification.Report));
        Assert.True(qualification.Report.Summary.Failed == 0, executionJson);
        Assert.True(qualification.Report.Summary.Inconclusive == 0, executionJson);
        Assert.NotNull(qualification.AcceptedBaselineComparison);
        Assert.True(
            qualification.AcceptedBaselineComparison.IsMatch,
            Encoding.UTF8.GetString(EpubCorpusBaselineJsonSerializer.Serialize(qualification.ObservedBaseline)));
        Assert.All(qualification.Publications, static item =>
        {
            Assert.Equal(EpubCorpusExecutionStatus.Passed, item.FlowStatus);
            Assert.Equal(EpubCheckEvidenceStatus.Disabled, item.EpubCheckStatus);
            Assert.Equal(EpubCorpusEvidenceRelationship.NotEvaluated, item.EvidenceRelationship);
            Assert.Equal(0, item.ExpectationFailureCount);
        });

        var localReportPath = Path.Combine(workspace.Root, "reports", "detailed.json");
        await EpubCorpusQualificationService.WriteLocalDetailedReportAsync(qualification, localReportPath);
        var localReport = await File.ReadAllTextAsync(localReportPath);
        Assert.Contains("\"included\": true", localReport, StringComparison.Ordinal);
        Assert.Contains("fidelityLostUnitCount", localReport, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, localReport, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(localReportPath)!, "*.tmp"));
    }

    [Fact]
    public void BaselineSerializer_IsDeterministicAndComparatorLocalizesSemanticChange()
    {
        var semantic = new EpubCorpusSemanticEvidence(
            ["chapter", "paragraph"], 2, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0);
        var entry = new EpubCorpusBaselineEntry(
            new EpubCorpusPublicationId("sample"), EpubCorpusExecutionStatus.Passed, "Epub3",
            2, 1, 2, 0, 0, 0, "urn:sample", new string('A', 64), semantic, ["EPUB_WARNING"]);
        var baseline = new EpubCorpusBaseline([entry]);
        var bytes = EpubCorpusBaselineJsonSerializer.Serialize(baseline);
        var roundTrip = EpubCorpusBaselineJsonSerializer.Deserialize(bytes);

        Assert.Equal(bytes, EpubCorpusBaselineJsonSerializer.Serialize(roundTrip));
        var changedSemantic = new EpubCorpusSemanticEvidence(
            ["paragraph", "chapter"], 2, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0);
        var changed = new EpubCorpusBaseline([new EpubCorpusBaselineEntry(
            entry.Id, entry.Status, entry.EpubVersion, entry.ManifestItemCount, entry.SpineItemCount,
            entry.ImportedNodeCount, entry.ImportedAssetCount, entry.ValidationDiagnosticCount,
            entry.FidelityLostUnitCount, entry.DocumentId, entry.CanonicalHash, changedSemantic,
            ["EPUB_WARNING", "NEW_INFORMATION"])]);
        var comparison = EpubCorpusBaselineComparer.Compare(baseline, changed);

        var difference = Assert.Single(comparison.Differences);
        Assert.Equal("semantic.orderedNodeIds", difference.Field);
        Assert.DoesNotContain(comparison.Differences, static item => item.Field.Contains("NEW_INFORMATION", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(bytes));
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    private sealed class PublicCorpusWorkspace : IDisposable
    {
        public PublicCorpusWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"flow-public-corpus-{Guid.NewGuid():N}")).FullName;
        }

        public string Root { get; }

        public EpubCorpusManifest LoadAndMaterializeManifest()
        {
            using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Corpus", "epub-corpus.json"));
            var read = new EpubCorpusManifestJsonSerializer().Read(source);
            Assert.True(read.IsSuccess, string.Join(Environment.NewLine, read.Diagnostics.Select(static item => item.Message)));
            var manifest = Assert.IsType<EpubCorpusManifest>(read.Manifest);
            var fixtures = EpubCorpusFixtureFactory.CreateAll();
            foreach (var publication in manifest.Publications)
            {
                var bytes = fixtures[publication.Id.Value];
                Assert.Equal(publication.Sha256.Value, Convert.ToHexString(SHA256.HashData(bytes)));
                var path = Path.Combine(Root, publication.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            return manifest;
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
