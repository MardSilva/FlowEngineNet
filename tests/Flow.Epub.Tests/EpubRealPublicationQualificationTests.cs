using System.Security.Cryptography;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubRealPublicationQualificationTests
{
    public const string PublicationPathEnvironmentVariable = "FLOW_EPUB_REAL_BOOK_PATH";
    public const string ReportPathEnvironmentVariable = "FLOW_EPUB_REAL_REPORT_PATH";

    [Fact]
    [Trait("Category", "EpubCorpusExternal")]
    public async Task ExplicitLocalPublication_QualifiesWithoutEnteringTheRepository()
    {
        var sourcePath = Environment.GetEnvironmentVariable(PublicationPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        sourcePath = Path.GetFullPath(sourcePath);
        Assert.True(File.Exists(sourcePath), $"EPUB not found: {sourcePath}");
        var repositoryRoot = FindRepositoryRoot();
        Assert.False(IsInside(repositoryRoot, sourcePath), "A private EPUB must remain outside the repository.");

        EpubPublicationInspection inspection;
        await using (var source = File.OpenRead(sourcePath))
        {
            inspection = await new EpubPublicationInspector().InspectAsync(source);
        }

        Assert.True(inspection.IsSuccess);
        var bytes = await File.ReadAllBytesAsync(sourcePath);
        var publication = new EpubCorpusPublication(
            new EpubCorpusPublicationId("local-real-epub"),
            "Local real EPUB",
            $"local:{PublicationPathEnvironmentVariable}",
            new EpubCorpusLicense(
                "Local non-redistributable publication",
                $"Explicitly supplied through {PublicationPathEnvironmentVariable}; bytes remain outside the repository"),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/local-real.epub",
            inspection.Package?.VersionFamily ?? EpubVersionFamily.Unknown,
            bytes.LongLength,
            [inspection.Package?.Language ?? "und"],
            ["xhtml"],
            ["spine"],
            [
                "canonical-hash-stable",
                "desktop-layout",
                "html-book-package",
                "import-success",
                "inspection-success",
                "mobile-layout",
                "roundtrip-stable",
                "valid-flow-document",
            ],
            [],
            new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes))));
        var manifest = new EpubCorpusManifest(EpubCorpusManifest.CurrentFormat, [publication]);
        var qualification = await new EpubCorpusQualificationService().QualifyAsync(
            manifest,
            new EpubCorpusDiscoveryOptions(repositoryRoot, Path.GetDirectoryName(sourcePath)));

        var result = Assert.Single(qualification.Report.Publications);
        Assert.Equal(EpubCorpusExecutionStatus.Passed, result.Status);
        Assert.True(qualification.IsDeterministic);
        Assert.Equal(0, result.Evidence.FidelityLostUnitCount);
        var chapters = Assert.IsType<EpubCorpusSemanticEvidence>(result.Evidence.Semantic).OrderedChapterIds;
        Assert.NotEmpty(chapters);
        Assert.False(string.IsNullOrWhiteSpace(chapters[0]));
        Assert.False(string.IsNullOrWhiteSpace(chapters[chapters.Length / 2]));
        Assert.False(string.IsNullOrWhiteSpace(chapters[^1]));

        var reportPath = Environment.GetEnvironmentVariable(ReportPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            reportPath = Path.GetFullPath(reportPath);
            Assert.False(IsInside(repositoryRoot, reportPath), "A private qualification report must remain outside the repository.");
            await EpubCorpusQualificationService.WriteLocalDetailedReportAsync(qualification, reportPath);
        }
    }

    private static bool IsInside(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(
            prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Flow.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("The Flow repository root was not found.");
    }
}
