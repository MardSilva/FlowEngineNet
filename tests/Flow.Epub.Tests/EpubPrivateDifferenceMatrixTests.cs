using System.Text;
using System.Text.Json;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubPrivateDifferenceMatrixTests
{
    [Fact]
    public void Create_ClassifiesEveryCategoryAndAppliesDocumentedPrecedence()
    {
        var report = new EpubPrivateQualificationReport(
            deterministicAcrossRepeatedRuns: true,
            [
                Publication("approved"),
                Publication("approximation",
                [
                    Diagnostic(EpubDiagnosticCodes.HeadingLevelNormalized, count: 2),
                    Diagnostic(EpubDiagnosticCodes.HeadingLevelNormalized),
                ]),
                Publication("unsupported", [Diagnostic(EpubDiagnosticCodes.LinkedImageTargetNotRepresentable)]),
                Publication("loss", evidence: Evidence(lost: 4)),
                Publication("broken", [Diagnostic(EpubDiagnosticCodes.InvalidReference)]),
                Publication("flow-error", [Diagnostic(EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch,
                    EpubCorpusExecutionDiagnosticSeverity.Error, EpubCorpusExecutionPhase.Integrity)],
                    EpubPrivateQualificationStatus.Failed),
                Publication("human-review", [Diagnostic("FUTURE001")], EpubPrivateQualificationStatus.Inconclusive),
            ]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(report, Hash('A'));

        Assert.Equal(7, matrix.Summary.TotalCandidates);
        Assert.Equal(1, matrix.Summary.Approved);
        Assert.Equal(1, matrix.Summary.ApprovedWithApproximations);
        Assert.Equal(1, matrix.Summary.UnsupportedContent);
        Assert.Equal(1, matrix.Summary.ContentLoss);
        Assert.Equal(1, matrix.Summary.BrokenSourceReference);
        Assert.Equal(1, matrix.Summary.FlowError);
        Assert.Equal(1, matrix.Summary.HumanReviewRequired);
        Assert.All(matrix.Candidates, static candidate => Assert.True(candidate.HumanReviewPending));
        var approximation = matrix.Candidates.Single(static item => item.Id.Value == "approximation");
        Assert.Equal(3, Assert.Single(approximation.Differences).Count);
        var unsupported = matrix.Candidates.Single(static item => item.Id.Value == "unsupported");
        Assert.Equal(EpubPrivateDifferenceMetric.References, Assert.Single(unsupported.Differences).Metric);
        Assert.Equal(EpubPrivateDifferenceCategory.UnsupportedContent, unsupported.OverallCategory);
        Assert.Equal(EpubPrivateDifferenceCategory.ContentLoss,
            matrix.Candidates.Single(static item => item.Id.Value == "loss").OverallCategory);
    }

    [Fact]
    public void Serialization_IsDeterministicUtf8LfAndContainsNoPhysicalLocation()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("candidate-b", [Diagnostic(EpubDiagnosticCodes.NoteResourceFallbackUsed)]),
            Publication("candidate-a", [Diagnostic(EpubDiagnosticCodes.LinkedImageTargetNotRepresentable)]),
        ]);
        var service = new EpubPrivateDifferenceMatrixService();

        var first = EpubPrivateDifferenceMatrixJsonSerializer.Serialize(service.Create(report, Hash('B')));
        var second = EpubPrivateDifferenceMatrixJsonSerializer.Serialize(service.Create(report, Hash('B')));
        var text = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", text, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(first);
        Assert.Equal(EpubPrivateDifferenceMatrix.CurrentFormat,
            json.RootElement.GetProperty("format").GetString());
        Assert.All(json.RootElement.GetProperty("candidates").EnumerateArray(), static candidate =>
            Assert.Equal("pending", candidate.GetProperty("humanReview").GetString()));
    }

    [Fact]
    public void QualificationDeserializer_RoundTripsAndRejectsUnknownFormatDuplicateCandidateAndAlteredSummary()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("candidate-a"),
            Publication("candidate-b"),
        ]);
        var bytes = EpubPrivateQualificationReportJsonSerializer.Serialize(report);

        var restored = EpubPrivateQualificationReportJsonSerializer.Deserialize(bytes);

        Assert.Equal(bytes, EpubPrivateQualificationReportJsonSerializer.Serialize(restored));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Throws<InvalidDataException>(() => EpubPrivateQualificationReportJsonSerializer.Deserialize(
            Encoding.UTF8.GetBytes(text.Replace(
                EpubPrivateQualificationReport.CurrentFormat,
                "flow-epub-private-qualification-9.9",
                StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => EpubPrivateQualificationReportJsonSerializer.Deserialize(
            Encoding.UTF8.GetBytes(text.Replace("candidate-b", "candidate-a", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => EpubPrivateQualificationReportJsonSerializer.Deserialize(
            Encoding.UTF8.GetBytes(text.Replace("\"total\": 2", "\"total\": 3", StringComparison.Ordinal))));
    }

    [Fact]
    public void Create_KeepsOtherCandidatesWhenOneContainsFailureOrUnknownEvidence()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("first", [Diagnostic("UNKNOWN001")], EpubPrivateQualificationStatus.Inconclusive),
            Publication("second"),
            Publication("third", evidence: Evidence(lost: 1), status: EpubPrivateQualificationStatus.Failed),
        ]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(report, Hash('C'));

        Assert.Equal(3, matrix.Candidates.Length);
        Assert.Equal(EpubPrivateDifferenceCategory.HumanReviewRequired, matrix.Candidates[0].OverallCategory);
        Assert.Equal(EpubPrivateDifferenceCategory.Approved, matrix.Candidates[1].OverallCategory);
        Assert.Equal(EpubPrivateDifferenceCategory.ContentLoss, matrix.Candidates[2].OverallCategory);
    }

    [Fact]
    public void Create_ClassifiesTransparentContainerTransformationAsApprovedEvidence()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("transformed",
            [
                Diagnostic(
                    EpubDiagnosticCodes.TransparentContainerTransformed,
                    EpubCorpusExecutionDiagnosticSeverity.Information,
                    count: 12),
            ]),
        ]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(report, Hash('F'));

        var candidate = Assert.Single(matrix.Candidates);
        var difference = Assert.Single(candidate.Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.Approved, candidate.OverallCategory);
        Assert.Equal(EpubPrivateDifferenceCategory.Approved, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.NoAutomaticDifference, difference.Cause);
        Assert.Equal(12, difference.Count);
    }

    [Fact]
    public void Create_ClassifiesRecoveredPathCaseMismatchAsReferenceApproximation()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("case-recovery",
            [
                Diagnostic(EpubDiagnosticCodes.ArchivePathCaseMismatchRecovered),
            ]),
        ]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(report, Hash('A'));

        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.ApprovedWithApproximations, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.SourceApproximation, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.References, difference.Metric);
    }

    [Fact]
    public void Create_ClassifiesUnrepresentedTableColumnMetadataAsTableApproximation()
    {
        var report = new EpubPrivateQualificationReport(true,
        [
            Publication("table-columns",
            [
                Diagnostic(EpubDiagnosticCodes.TableColumnMetadataNotRepresented, count: 24),
            ]),
        ]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(report, Hash('T'));

        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.ApprovedWithApproximations, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.SourceApproximation, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.Tables, difference.Metric);
        Assert.Equal(24, difference.Count);
    }

    [Fact]
    public async Task AtomicWrite_CancellationPreservesExistingDestinationAndLeavesNoTemporaryFile()
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"flow-private-matrix-tests-{Guid.NewGuid():N}"));
        var output = Path.Combine(directory.FullName, "matrix.json");
        await File.WriteAllTextAsync(output, "existing");
        var matrix = new EpubPrivateDifferenceMatrixService().Create(
            new EpubPrivateQualificationReport(true, [Publication("candidate")]),
            Hash('E'));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                EpubPrivateDifferenceMatrixJsonSerializer.WriteAtomicallyAsync(matrix, output, cancellation.Token));

            Assert.Equal("existing", await File.ReadAllTextAsync(output));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, ".matrix.json.*.tmp"));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static EpubPrivateQualificationItem Publication(
        string id,
        IEnumerable<EpubPrivateQualificationDiagnosticCount>? diagnostics = null,
        EpubPrivateQualificationStatus status = EpubPrivateQualificationStatus.Passed,
        EpubPrivateQualificationEvidence? evidence = null) => new(
        new EpubCorpusPublicationId(id),
        Hash(id[0]),
        EpubPrivateInventoryStatus.ReviewRequired,
        status,
        eligible: true,
        stableAcrossRepeatedRuns: status != EpubPrivateQualificationStatus.Nondeterministic,
        Enum.GetValues<EpubCorpusExecutionPhase>(),
        evidence ?? Evidence(),
        diagnostics ?? []);

    private static EpubPrivateQualificationEvidence Evidence(long lost = 0) => new(
        1, 1, 1, 0, 0, 10, lost, new string('D', 64), 1, 1, 1, 2, 4, 10,
        1, 1, 1, 0, 0, 0, 0, 0, 0, 0);

    private static EpubPrivateQualificationDiagnosticCount Diagnostic(
        string code,
        EpubCorpusExecutionDiagnosticSeverity severity = EpubCorpusExecutionDiagnosticSeverity.Warning,
        EpubCorpusExecutionPhase? phase = EpubCorpusExecutionPhase.Fidelity,
        int count = 1) => new(code, severity, phase, count);

    private static EpubCorpusSha256 Hash(char value) => new(new string(Uri.IsHexDigit(value) ? value : 'A', 64));
}
