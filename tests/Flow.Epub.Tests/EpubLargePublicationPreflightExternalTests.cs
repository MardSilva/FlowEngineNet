using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationPreflightExternalTests
{
    public const string CandidatesEnvironmentVariable = "FLOW_EPUB_LARGE_CANDIDATES";
    public const string ReportPathEnvironmentVariable = "FLOW_EPUB_LARGE_PREFLIGHT_REPORT";

    [Fact]
    [Trait("Category", "EpubLargePreflightExternal")]
    public async Task ExplicitLocalCandidates_SelectWithoutEnteringRepository()
    {
        var configured = Environment.GetEnvironmentVariable(CandidatesEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var candidates = configured.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseCandidate)
            .ToArray();
        Assert.NotEmpty(candidates);
        var repositoryRoot = FindRepositoryRoot();
        var report = await new EpubLargePublicationPreflightService().EvaluateAsync(candidates, repositoryRoot);

        Assert.True(
            report.Status == EpubLargePublicationSelectionStatus.Selected,
            string.Join(Environment.NewLine, report.Candidates.SelectMany(static candidate =>
                candidate.Diagnostics.Select(diagnostic => $"{candidate.Id.Value}: {diagnostic.Code}/{diagnostic.Severity}"))));
        var reportPath = Environment.GetEnvironmentVariable(ReportPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            reportPath = Path.GetFullPath(reportPath);
            Assert.False(IsInside(repositoryRoot, reportPath), "A local preflight report must remain outside the repository.");
            await EpubLargePublicationPreflightReportJsonSerializer.WriteAtomicallyAsync(report, reportPath);
        }
    }

    private static EpubLargePublicationCandidate ParseCandidate(string configured)
    {
        var separator = configured.IndexOf('=');
        Assert.True(separator > 0 && separator < configured.Length - 1,
            $"{CandidatesEnvironmentVariable} entries must use neutral-id=absolute-epub-path separated by '|'.");
        var id = new EpubCorpusPublicationId(configured[..separator]);
        var path = Path.GetFullPath(configured[(separator + 1)..]);
        Assert.True(File.Exists(path), $"Configured candidate '{id.Value}' was not found.");
        return new EpubLargePublicationCandidate(id, path, legalUseDeclared: true, drmFreeDeclared: true);
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
