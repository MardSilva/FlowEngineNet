using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flow.Epub.Corpus;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubPrivateVisualReviewTests
{
    [Fact]
    public async Task Generate_CreatesNeutralIndexAndContinuesAfterCandidateFailure()
    {
        using var workspace = new TemporaryWorkspace();
        var firstBytes = Encoding.UTF8.GetBytes("first private epub");
        var secondBytes = Encoding.UTF8.GetBytes("second private epub");
        await File.WriteAllBytesAsync(Path.Combine(workspace.Source, "private-title.epub"), firstBytes);
        await File.WriteAllBytesAsync(Path.Combine(workspace.Source, "another-title.epub"), secondBytes);
        var firstHash = Hash(firstBytes);
        var secondHash = Hash(secondBytes);
        var qualification = new EpubPrivateQualificationReport(true,
        [
            Publication("candidate-001", firstHash),
            Publication("candidate-002", secondHash),
            Publication("candidate-003", null, EpubPrivateQualificationStatus.SkippedProtected, eligible: false),
        ]);
        var service = new EpubPrivateVisualReviewService(new FakeGenerator("candidate-002"));

        var result = await service.GenerateAsync(
            qualification,
            new EpubCorpusSha256(new string('A', 64)),
            new EpubPrivateVisualReviewOptions(
                workspace.Source,
                workspace.Output,
                workspace.Repository,
                legalUseDeclared: true,
                drmFreeDeclared: true,
                HtmlBookUiLanguage.PortugueseBrazil));

        Assert.Equal(3, result.Report.Summary.Total);
        Assert.Equal(1, result.Report.Summary.Generated);
        Assert.Equal(1, result.Report.Summary.Failed);
        Assert.Equal(1, result.Report.Summary.Skipped);
        Assert.True(File.Exists(Path.Combine(workspace.Output, "index.html")));
        Assert.True(File.Exists(Path.Combine(workspace.Output, "candidate-001", "review.html")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Output, "candidate-002")));
        var json = await File.ReadAllBytesAsync(Path.Combine(workspace.Output, "corpus-review.json"));
        var text = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain(workspace.Source, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-title", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, EpubPrivateVisualReviewReportJsonSerializer.Serialize(result.Report));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(EpubPrivateVisualReviewReport.CurrentFormat,
            document.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Generate_RepeatedRunsProduceTheSamePathFreeReport()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.UTF8.GetBytes("deterministic candidate");
        await File.WriteAllBytesAsync(Path.Combine(workspace.Source, "book.epub"), bytes);
        var qualification = new EpubPrivateQualificationReport(true,
            [Publication("candidate-001", Hash(bytes))]);
        var service = new EpubPrivateVisualReviewService(new FakeGenerator());

        var first = await service.GenerateAsync(
            qualification,
            new EpubCorpusSha256(new string('B', 64)),
            Options(workspace, Path.Combine(workspace.Root, "first")));
        var second = await service.GenerateAsync(
            qualification,
            new EpubCorpusSha256(new string('B', 64)),
            Options(workspace, Path.Combine(workspace.Root, "second")));

        Assert.Equal(
            EpubPrivateVisualReviewReportJsonSerializer.Serialize(first.Report),
            EpubPrivateVisualReviewReportJsonSerializer.Serialize(second.Report));
    }

    private static EpubPrivateVisualReviewOptions Options(TemporaryWorkspace workspace, string output) => new(
        workspace.Source,
        output,
        workspace.Repository,
        legalUseDeclared: true,
        drmFreeDeclared: true);

    private static EpubPrivateQualificationItem Publication(
        string id,
        EpubCorpusSha256? hash,
        EpubPrivateQualificationStatus status = EpubPrivateQualificationStatus.Passed,
        bool eligible = true) => new(
        new EpubCorpusPublicationId(id),
        hash,
        EpubPrivateInventoryStatus.ReviewRequired,
        status,
        eligible,
        stableAcrossRepeatedRuns: status == EpubPrivateQualificationStatus.Passed,
        Enum.GetValues<EpubCorpusExecutionPhase>(),
        EpubPrivateQualificationEvidence.Empty,
        []);

    private static EpubCorpusSha256 Hash(byte[] bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)));

    private sealed class FakeGenerator(string? failedId = null) : IEpubLargePublicationReviewPackageGenerator
    {
        public async Task<EpubLargePublicationReviewResult> GenerateAsync(
            EpubLargePublicationCandidate candidate,
            EpubLargePublicationReviewOptions options,
            CancellationToken cancellationToken = default)
        {
            if (candidate.Id.Value == failedId)
            {
                throw new InvalidDataException("Injected isolated candidate failure.");
            }

            Directory.CreateDirectory(options.OutputDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(options.OutputDirectory, "review.html"),
                "<!doctype html>\n",
                cancellationToken);
            var canonical = new EpubCorpusSha256(new string('C', 64));
            return new EpubLargePublicationReviewResult(
                options.OutputDirectory,
                options.ExpectedSourceSha256,
                canonical,
                new EpubCorpusSha256(new string('D', 64)),
                new EpubCorpusSha256(new string('E', 64)),
                [new EpubLargePublicationReviewSample(EpubLargePublicationSamplePosition.Beginning,
                    new Flow.Core.NodeId("chapter-start"), "mobile/start", "desktop/start")],
                [new EpubLargePublicationReviewTarget("image", new Flow.Core.NodeId("figure-1"),
                    "mobile/image", "desktop/image")]);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-private-review-{Guid.NewGuid():N}");
            Source = Path.Combine(Root, "source");
            Output = Path.Combine(Root, "output");
            Repository = Path.Combine(Root, "repository");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Repository);
        }

        public string Root { get; }

        public string Source { get; }

        public string Output { get; }

        public string Repository { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
