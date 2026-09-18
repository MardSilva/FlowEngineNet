using System.Security.Cryptography;
using System.Text;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationPreflightTests
{
    [Fact]
    public async Task EvaluateAsync_SelectsLongStructurallyRichCandidateDeterministically()
    {
        using var workspace = new PreflightWorkspace();
        var shorter = workspace.AddEpub("candidate-b", CreateLongEpub(20));
        var longer = workspace.AddEpub("candidate-a", CreateLongEpub(24));
        var service = new EpubLargePublicationPreflightService();

        var first = await service.EvaluateAsync([shorter, longer], workspace.RepositoryRoot);
        var second = await service.EvaluateAsync([longer, shorter], workspace.RepositoryRoot);

        Assert.Equal(EpubLargePublicationSelectionStatus.Selected, first.Status);
        Assert.Equal(longer.Id, first.SelectedCandidateId);
        Assert.Equal(first.SelectedCandidateId, second.SelectedCandidateId);
        Assert.Equal(
            EpubLargePublicationPreflightReportJsonSerializer.Serialize(first),
            EpubLargePublicationPreflightReportJsonSerializer.Serialize(second));
        var selected = Assert.Single(first.Candidates, item => item.Id == longer.Id);
        Assert.Equal(EpubLargePublicationCandidateStatus.Suitable, selected.Status);
        Assert.Equal(24, selected.LinearSpineItemCount);
        Assert.True(selected.Resources.XhtmlBytes > 0);
        Assert.Equal(EpubPreflightFeatureStatus.Present, selected.Features.TableOfContents);
        Assert.Equal(EpubPreflightFeatureStatus.Present, selected.Features.Images);
        Assert.Equal(EpubPreflightFeatureStatus.Unknown, selected.Features.Links);
        Assert.Equal(EpubPreflightFeatureStatus.Unknown, selected.Features.Notes);
        Assert.Equal(EpubPreflightFeatureStatus.Unknown, selected.Features.Tables);
    }

    [Fact]
    public async Task EvaluateAsync_SmallInvalidAndStructurallyPoorCandidatesAreInconclusive()
    {
        using var workspace = new PreflightWorkspace();
        using var smallEpub = MinimalEpubFactory.Create();
        var small = workspace.AddEpub("small", smallEpub.ToArray());
        var poor = workspace.AddEpub("poor", CreatePoorEpub());
        var invalid = workspace.AddBytes("invalid", "not a zip"u8.ToArray());

        var report = await new EpubLargePublicationPreflightService().EvaluateAsync(
            [small, invalid, poor],
            workspace.RepositoryRoot);

        Assert.Equal(EpubLargePublicationSelectionStatus.Inconclusive, report.Status);
        Assert.Null(report.SelectedCandidateId);
        Assert.Contains(report.Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.NoSuitableCandidate);
        Assert.Equal(EpubLargePublicationCandidateStatus.Rejected, report.Candidates.Single(item => item.Id == invalid.Id).Status);
        Assert.Contains(report.Candidates.Single(item => item.Id == invalid.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.InspectionFailed);
        Assert.Contains(report.Candidates.Single(item => item.Id == small.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.MissingNavigation
            || item.Code == EpubLargePublicationPreflightDiagnosticCodes.InsufficientLengthEvidence);
        Assert.Contains(report.Candidates.Single(item => item.Id == poor.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.InsufficientResourceVariety);
    }

    [Fact]
    public async Task EvaluateAsync_CountsLegacyTrueTypeMediaTypeAsFont()
    {
        using var workspace = new PreflightWorkspace();
        var candidate = workspace.AddEpub("legacy-font", CreateLongEpub(20, includeLegacyFont: true));

        var report = await new EpubLargePublicationPreflightService().EvaluateAsync(
            [candidate],
            workspace.RepositoryRoot);

        var result = Assert.Single(report.Candidates);
        Assert.Equal(1, result.Resources.Fonts);
        Assert.Equal(0, result.Resources.Other);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsUndeclaredUseRepositoryInputSymlinkAndLimitViolation()
    {
        using var workspace = new PreflightWorkspace();
        var bytes = CreateLongEpub(20);
        var undeclaredPath = workspace.WriteExternal("undeclared.epub", bytes);
        var undeclared = new EpubLargePublicationCandidate(new EpubCorpusPublicationId("undeclared"), undeclaredPath, false, false);
        var repositoryPath = workspace.WriteRepository("inside.epub", bytes);
        var repository = new EpubLargePublicationCandidate(new EpubCorpusPublicationId("inside"), repositoryPath, true, true);
        var oversizedPath = workspace.WriteExternal("oversized.epub", bytes);
        var oversized = new EpubLargePublicationCandidate(new EpubCorpusPublicationId("oversized"), oversizedPath, true, true);
        var limited = new EpubLargePublicationPreflightService(limits: new EpubImportLimits(maximumArchiveBytes: 64));

        var policyReport = await new EpubLargePublicationPreflightService().EvaluateAsync(
            [undeclared, repository],
            workspace.RepositoryRoot);
        var limitReport = await limited.EvaluateAsync([oversized], workspace.RepositoryRoot);

        Assert.All(policyReport.Candidates, static item => Assert.Equal(EpubLargePublicationCandidateStatus.Rejected, item.Status));
        Assert.Contains(policyReport.Candidates.Single(item => item.Id == undeclared.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.LegalUseNotDeclared);
        Assert.Contains(policyReport.Candidates.Single(item => item.Id == undeclared.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.DrmFreeNotDeclared);
        Assert.Contains(policyReport.Candidates.Single(item => item.Id == repository.Id).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.CandidateInsideRepository);
        Assert.Contains(Assert.Single(limitReport.Candidates).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.ArchiveLimitExceeded);

        var linkPath = Path.Combine(workspace.ExternalRoot, "linked.epub");
        try
        {
            File.CreateSymbolicLink(linkPath, oversizedPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var linked = new EpubLargePublicationCandidate(new EpubCorpusPublicationId("linked"), linkPath, true, true);
        var linkReport = await new EpubLargePublicationPreflightService().EvaluateAsync([linked], workspace.RepositoryRoot);
        Assert.Contains(Assert.Single(linkReport.Candidates).Diagnostics, static item =>
            item.Code == EpubLargePublicationPreflightDiagnosticCodes.CandidatePathUnsafe);
    }

    [Fact]
    public async Task Report_IsDeterministicUtf8LfAndDoesNotExposePathOrBookMetadata()
    {
        using var workspace = new PreflightWorkspace("person-name");
        const string privateTitle = "Private title that must not enter the report";
        var candidate = workspace.AddEpub("neutral-book", CreateLongEpub(20, privateTitle));
        var report = await new EpubLargePublicationPreflightService().EvaluateAsync([candidate], workspace.RepositoryRoot);

        var first = EpubLargePublicationPreflightReportJsonSerializer.Serialize(report);
        var second = EpubLargePublicationPreflightReportJsonSerializer.Serialize(report);
        var text = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.DoesNotContain(workspace.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("person-name", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateTitle, text, StringComparison.Ordinal);
        Assert.DoesNotContain("creator-private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("path", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Matches("[A-F0-9]{64}", Assert.Single(report.Candidates).Sha256?.Value);
    }

    [Fact]
    public async Task AtomicWrite_CancellationPreservesDestinationAndDeletesTemporaryFile()
    {
        using var workspace = new PreflightWorkspace();
        var report = new EpubLargePublicationPreflightReport(
            EpubLargePublicationSelectionStatus.Inconclusive,
            null,
            new EpubLargePublicationPreflightCriteria(),
            [],
            [new EpubLargePublicationPreflightDiagnostic(
                EpubLargePublicationPreflightDiagnosticCodes.NoSuitableCandidate,
                EpubLargePublicationPreflightSeverity.Warning)]);
        var output = Path.Combine(workspace.OutputRoot, "preflight.json");
        await File.WriteAllTextAsync(output, "existing");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EpubLargePublicationPreflightReportJsonSerializer.WriteAtomicallyAsync(
                report,
                output,
                cancellation.Token));

        Assert.Equal("existing", await File.ReadAllTextAsync(output));
        Assert.Single(Directory.EnumerateFiles(workspace.OutputRoot));
    }

    [Fact]
    public async Task EvaluateAsync_CancellationIsPropagated()
    {
        using var workspace = new PreflightWorkspace();
        var candidate = workspace.AddEpub("candidate", CreateLongEpub(20));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EpubLargePublicationPreflightService().EvaluateAsync(
                [candidate],
                workspace.RepositoryRoot,
                cancellationToken: cancellation.Token));
    }

    private static byte[] CreateLongEpub(
        int chapterCount,
        string title = "Synthetic long fixture",
        bool includeLegacyFont = false)
    {
        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        var additional = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index <= chapterCount; index++)
        {
            manifest.Append($"<item id=\"chapter-{index}\" href=\"text/chapter-{index}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spine.Append($"<itemref idref=\"chapter-{index}\"/>");
            if (index > 1)
            {
                additional[$"EPUB/text/chapter-{index}.xhtml"] = Chapter(index);
            }
        }

        manifest.Append("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        manifest.Append("<item id=\"image\" href=\"images/flow.png\" media-type=\"image/png\"/>");
        if (includeLegacyFont)
        {
            manifest.Append("<item id=\"font\" href=\"fonts/book.ttf\" media-type=\"application/x-font-truetype\"/>");
        }

        additional["EPUB/nav.xhtml"] = """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Navigation</title></head><body><nav epub:type="toc"><ol><li><a href="text/chapter-1.xhtml">Start</a></li></ol></nav></body></html>
            """;
        var package = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:large-preflight</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator>creator-private</dc:creator>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>{manifest}</manifest>
              <spine>{spine}</spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Chapter(1),
            includeSecondChapter: false,
            additionalTextEntries: additional,
            additionalBinaryEntries: includeLegacyFont
                ? new Dictionary<string, byte[]> { ["EPUB/fonts/book.ttf"] = [0x00, 0x01, 0x02] }
                : null);
        return epub.ToArray();
    }

    private static byte[] CreatePoorEpub()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:poor</dc:identifier>
                <dc:title>Poor fixture</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest><item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml"/></manifest>
              <spine><itemref idref="chapter"/></spine>
            </package>
            """;
        using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Chapter(1),
            includeSecondChapter: false,
            includeImage: false);
        return epub.ToArray();
    }

    private static string Chapter(int index) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter {index}</title></head><body><h1 id="chapter-{index}">Chapter {index}</h1><p>Project-owned synthetic content.</p></body></html>
        """;

    private sealed class PreflightWorkspace : IDisposable
    {
        public PreflightWorkspace(string? segment = null)
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-large-preflight-{segment ?? "tests"}-{Guid.NewGuid():N}");
            RepositoryRoot = Directory.CreateDirectory(Path.Combine(Root, "repository")).FullName;
            ExternalRoot = Directory.CreateDirectory(Path.Combine(Root, "external")).FullName;
            OutputRoot = Directory.CreateDirectory(Path.Combine(Root, "output")).FullName;
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string ExternalRoot { get; }

        public string OutputRoot { get; }

        public EpubLargePublicationCandidate AddEpub(string id, byte[] bytes) => new(
            new EpubCorpusPublicationId(id),
            WriteExternal($"private-{id}.epub", bytes),
            legalUseDeclared: true,
            drmFreeDeclared: true);

        public EpubLargePublicationCandidate AddBytes(string id, byte[] bytes) => AddEpub(id, bytes);

        public string WriteExternal(string name, byte[] bytes)
        {
            var path = Path.Combine(ExternalRoot, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public string WriteRepository(string name, byte[] bytes)
        {
            var path = Path.Combine(RepositoryRoot, name);
            File.WriteAllBytes(path, bytes);
            return path;
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
