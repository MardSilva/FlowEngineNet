using Flow.Epub.Corpus;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationGateExternalTests
{
    public const string CandidateEnvironmentVariable = "FLOW_EPUB_LARGE_GATE_CANDIDATE";
    public const string Sha256EnvironmentVariable = "FLOW_EPUB_LARGE_GATE_SHA256";
    public const string ReportEnvironmentVariable = "FLOW_EPUB_LARGE_GATE_REPORT";
    public const string ReviewOutputEnvironmentVariable = "FLOW_EPUB_LARGE_REVIEW_OUTPUT";

    [Fact]
    [Trait("Category", "EpubLargeGateExternal")]
    public async Task ExplicitLocalCandidate_RunsAutomaticGateAndPreparesHumanReview()
    {
        var configuredCandidate = Environment.GetEnvironmentVariable(CandidateEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredCandidate))
        {
            return;
        }

        var expectedSha256 = Required(Sha256EnvironmentVariable);
        var reportPath = Path.GetFullPath(Required(ReportEnvironmentVariable));
        var reviewOutput = Path.GetFullPath(Required(ReviewOutputEnvironmentVariable));
        var repositoryRoot = FindRepositoryRoot();
        Assert.False(IsInside(repositoryRoot, reportPath), "The local gate report must remain outside the repository.");
        Assert.False(IsInside(repositoryRoot, reviewOutput), "The local review package must remain outside the repository.");

        var candidate = ParseCandidate(configuredCandidate);
        var sourceHash = new EpubCorpusSha256(expectedSha256);
        var options = new EpubLargePublicationGateOptions(candidate.Id, sourceHash);
        var report = await new EpubLargePublicationGate().ExecuteAsync(candidate, options);
        await EpubLargePublicationGateReportJsonSerializer.WriteAtomicallyAsync(
            report,
            reportPath,
            includeNonDeterministicEnvironment: true);

        var reviewOptions = new EpubLargePublicationReviewOptions(
            candidate.Id,
            sourceHash,
            reviewOutput,
            repositoryRoot,
            HtmlBookUiLanguage.PortugueseBrazil);
        await new EpubLargePublicationReviewPackageGenerator().GenerateAsync(candidate, reviewOptions);

        var failedAutomaticPhases = report.Result.Phases
            .Where(static phase => phase.Kind is not EpubLargePublicationGatePhaseKind.HumanReview)
            .Where(static phase => phase.Status is not EpubLargePublicationGateStatus.Passed
                and not EpubLargePublicationGateStatus.PassedWithWarnings)
            .ToArray();
        Assert.Empty(failedAutomaticPhases);
        Assert.Equal(EpubLargePublicationGateStatus.Inconclusive, report.Result.Status);
        Assert.Equal(
            EpubLargePublicationGateStatus.Inconclusive,
            report.Result.Phases.Single(static phase => phase.Kind == EpubLargePublicationGatePhaseKind.HumanReview).Status);
    }

    private static EpubLargePublicationCandidate ParseCandidate(string configured)
    {
        var separator = configured.IndexOf('=');
        Assert.True(
            separator > 0 && separator < configured.Length - 1,
            $"{CandidateEnvironmentVariable} must use neutral-id=absolute-epub-path.");
        var id = new EpubCorpusPublicationId(configured[..separator]);
        var path = configured[(separator + 1)..];
        Assert.True(Path.IsPathFullyQualified(path), "The large-gate EPUB path must be absolute.");
        Assert.True(File.Exists(path), $"Configured candidate '{id.Value}' was not found.");
        return new EpubLargePublicationCandidate(id, path, legalUseDeclared: true, drmFreeDeclared: true);
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"{name} must be set when the external large gate is enabled.");
        return value;
    }

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedRoot, Path.TrimEndingDirectorySeparator(normalizedPath), comparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
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
