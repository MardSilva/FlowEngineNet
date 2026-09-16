using System.Text;
using System.Text.Json;
using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationGateContractTests
{
    [Fact]
    public void PartialFailure_RepresentsEveryPlannedPhaseWithoutInventingSuccess()
    {
        var report = CreateReport(
            observations: null,
            phases:
            [
                new(EpubLargePublicationGatePhaseKind.Preflight, EpubLargePublicationGateStatus.Passed),
                new(EpubLargePublicationGatePhaseKind.Inspection, EpubLargePublicationGateStatus.Failed),
                new(EpubLargePublicationGatePhaseKind.Import, EpubLargePublicationGateStatus.Skipped),
            ]);

        Assert.Equal(
            Enum.GetValues<EpubLargePublicationGatePhaseKind>().Length,
            report.Result.Phases.Length);
        Assert.Equal(
            EpubLargePublicationGateStatus.NotStarted,
            report.Result.Phases.Single(item => item.Kind == EpubLargePublicationGatePhaseKind.Fidelity).Status);
        Assert.Equal(
            EpubLargePublicationGateStatus.Skipped,
            report.Result.Phases.Single(item => item.Kind == EpubLargePublicationGatePhaseKind.Import).Status);
        Assert.DoesNotContain(
            report.Result.Phases,
            item => item.Kind > EpubLargePublicationGatePhaseKind.Inspection
                && item.Status == EpubLargePublicationGateStatus.Passed);
    }

    [Fact]
    public void Checks_KeepAutomaticAndHumanReviewOutcomesSeparate()
    {
        var report = CreateReport();

        Assert.All(
            report.Result.AutomaticChecks,
            static item => Assert.Equal(EpubLargePublicationGateCheckKind.Automatic, item.Kind));
        Assert.All(
            report.Result.HumanReviewItems,
            static item => Assert.Equal(EpubLargePublicationGateCheckKind.HumanReview, item.Kind));
        Assert.Contains(report.Result.AutomaticChecks, static item => item.Status == EpubLargePublicationGateStatus.Failed);
        Assert.Contains(report.Result.HumanReviewItems, static item => item.Status == EpubLargePublicationGateStatus.Inconclusive);
        Assert.Contains(report.Result.Diagnostics, static item => item.Severity == EpubLargePublicationGateDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Serialization_IsDeterministicVersionedAndReadableByGenericTools()
    {
        var first = CreateReport(reverseInputs: false);
        var second = CreateReport(reverseInputs: true);

        var firstBytes = EpubLargePublicationGateReportJsonSerializer.Serialize(first);
        var secondBytes = EpubLargePublicationGateReportJsonSerializer.Serialize(second);

        Assert.Equal(firstBytes, secondBytes);
        using var json = JsonDocument.Parse(firstBytes);
        Assert.Equal(
            EpubLargePublicationGateReport.CurrentFormat,
            json.RootElement.GetProperty("format").GetString());
        Assert.Equal(
            Enum.GetValues<EpubLargePublicationGatePhaseKind>().Length,
            json.RootElement.GetProperty("result").GetProperty("phases").GetArrayLength());
        Assert.Equal(
            JsonValueKind.Object,
            json.RootElement.GetProperty("result").GetProperty("deterministicEvidence").ValueKind);
    }

    [Fact]
    public void Serialization_UsesUtf8WithoutBomAndLfOnly()
    {
        var bytes = EpubLargePublicationGateReportJsonSerializer.Serialize(CreateReport());
        var text = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentalObservations_AreExcludedFromDeterministicSerializationByDefault()
    {
        var low = CreateReport(new EpubLargePublicationGateEnvironmentObservations(10, 20, 30));
        var high = CreateReport(new EpubLargePublicationGateEnvironmentObservations(999, 888, 777));

        Assert.Equal(
            EpubLargePublicationGateReportJsonSerializer.Serialize(low),
            EpubLargePublicationGateReportJsonSerializer.Serialize(high));
        Assert.NotEqual(
            EpubLargePublicationGateReportJsonSerializer.Serialize(low, includeNonDeterministicEnvironment: true),
            EpubLargePublicationGateReportJsonSerializer.Serialize(high, includeNonDeterministicEnvironment: true));

        var text = Encoding.UTF8.GetString(
            EpubLargePublicationGateReportJsonSerializer.Serialize(high, includeNonDeterministicEnvironment: true));
        Assert.Contains("excluded-from-deterministic-comparison", text, StringComparison.Ordinal);
        Assert.Contains("\"totalDurationTicks\": 999", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialization_DoesNotExposePathsLicensedTextOrHtml()
    {
        var text = Encoding.UTF8.GetString(EpubLargePublicationGateReportJsonSerializer.Serialize(CreateReport()));

        Assert.DoesNotContain("C:\\Users\\private-reader", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("licensed chapter text", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outputPath", text, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => new EpubLargePublicationGateDiagnostic(
            "C:\\PRIVATE\\BOOK.EPUB",
            EpubLargePublicationGateDiagnosticSeverity.Error,
            EpubLargePublicationGatePhaseKind.Import));
    }

    [Fact]
    public async Task AtomicWrite_WritesCompleteReportAndLeavesNoTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, "gate.json");

        await EpubLargePublicationGateReportJsonSerializer.WriteAtomicallyAsync(CreateReport(), output);

        Assert.Equal(
            EpubLargePublicationGateReportJsonSerializer.Serialize(CreateReport()),
            await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.EnumerateFiles(workspace.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task AtomicWrite_CancellationPreservesExistingDestinationAndLeavesNoTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var output = Path.Combine(workspace.Path, "gate.json");
        await File.WriteAllTextAsync(output, "previous");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EpubLargePublicationGateReportJsonSerializer.WriteAtomicallyAsync(
                CreateReport(),
                output,
                cancellationToken: cancellation.Token));

        Assert.Equal("previous", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.EnumerateFiles(workspace.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Contract_RejectsAmbiguousChecksAndDuplicatePhases()
    {
        Assert.Throws<ArgumentException>(() => new EpubLargePublicationGateResult(
            EpubLargePublicationGateStatus.Failed,
            new EpubLargePublicationGateEvidence(),
            [
                new(EpubLargePublicationGatePhaseKind.Import, EpubLargePublicationGateStatus.Failed),
                new(EpubLargePublicationGatePhaseKind.Import, EpubLargePublicationGateStatus.Skipped),
            ],
            [],
            [],
            []));

        Assert.Throws<ArgumentException>(() => new EpubLargePublicationGateResult(
            EpubLargePublicationGateStatus.Inconclusive,
            new EpubLargePublicationGateEvidence(),
            [],
            [new("same-id", EpubLargePublicationGatePhaseKind.Validation, EpubLargePublicationGateCheckKind.Automatic, EpubLargePublicationGateStatus.Passed)],
            [new("same-id", EpubLargePublicationGatePhaseKind.HumanReview, EpubLargePublicationGateCheckKind.HumanReview, EpubLargePublicationGateStatus.Inconclusive)],
            []));
    }

    [Fact]
    public void GateInterface_HasNoCliOrTestFrameworkDependency()
    {
        var assemblyReferences = typeof(IEpubLargePublicationGate).Assembly
            .GetReferencedAssemblies()
            .Select(static item => item.Name)
            .ToArray();

        Assert.DoesNotContain("Flow.Cli", assemblyReferences);
        Assert.DoesNotContain("xunit.core", assemblyReferences);
    }

    private static EpubLargePublicationGateReport CreateReport(
        EpubLargePublicationGateEnvironmentObservations? observations = null,
        IEnumerable<EpubLargePublicationGatePhase>? phases = null,
        bool reverseInputs = false)
    {
        var automatic = new[]
        {
            new EpubLargePublicationGateCheck(
                "validation.document",
                EpubLargePublicationGatePhaseKind.Validation,
                EpubLargePublicationGateCheckKind.Automatic,
                EpubLargePublicationGateStatus.PassedWithWarnings),
            new EpubLargePublicationGateCheck(
                "inspection.resources",
                EpubLargePublicationGatePhaseKind.Inspection,
                EpubLargePublicationGateCheckKind.Automatic,
                EpubLargePublicationGateStatus.Failed),
        };
        var human = new[]
        {
            new EpubLargePublicationGateCheck(
                "review.end",
                EpubLargePublicationGatePhaseKind.HumanReview,
                EpubLargePublicationGateCheckKind.HumanReview,
                EpubLargePublicationGateStatus.Inconclusive),
            new EpubLargePublicationGateCheck(
                "review.beginning",
                EpubLargePublicationGatePhaseKind.HumanReview,
                EpubLargePublicationGateCheckKind.HumanReview,
                EpubLargePublicationGateStatus.Skipped),
        };
        var diagnostics = new[]
        {
            new EpubLargePublicationGateDiagnostic(
                "ELG002",
                EpubLargePublicationGateDiagnosticSeverity.Warning,
                EpubLargePublicationGatePhaseKind.Validation),
            new EpubLargePublicationGateDiagnostic(
                "ELG001",
                EpubLargePublicationGateDiagnosticSeverity.Error,
                EpubLargePublicationGatePhaseKind.Inspection),
        };
        var result = new EpubLargePublicationGateResult(
            EpubLargePublicationGateStatus.Failed,
            new EpubLargePublicationGateEvidence(
                EpubVersion: EpubVersionFamily.Epub3,
                SourceEpubBytes: 700_000,
                ManifestItemCount: 120,
                SpineItemCount: 80,
                ChapterCount: 75,
                ImportedNodeCount: 4_500,
                ImportedAssetCount: 25,
                ImportedCharacterCount: 800_000,
                ValidationDiagnosticCount: 2,
                FidelitySourceUnitCount: 9_000,
                FidelityLostUnitCount: 1,
                FlowJsonBytes: 1_200_000,
                CanonicalHash: new EpubCorpusSha256(new string('A', 64)),
                ReadingOrderHash: new EpubCorpusSha256(new string('B', 64)),
                AnchorSetHash: new EpubCorpusSha256(new string('C', 64)),
                MobileLayoutNodeCount: 4_500,
                MobileHtmlFileCount: 80,
                MobileHtmlBytes: 2_000_000,
                DesktopLayoutNodeCount: 4_500,
                DesktopHtmlFileCount: 80,
                DesktopHtmlBytes: 2_100_000),
            phases ??
            [
                new(EpubLargePublicationGatePhaseKind.Inspection, EpubLargePublicationGateStatus.Failed),
                new(EpubLargePublicationGatePhaseKind.Preflight, EpubLargePublicationGateStatus.Passed),
            ],
            reverseInputs ? automatic.Reverse() : automatic,
            reverseInputs ? human.Reverse() : human,
            reverseInputs ? diagnostics.Reverse() : diagnostics,
            observations);
        return new EpubLargePublicationGateReport(
            new EpubLargePublicationGateOptions(
                new EpubCorpusPublicationId("candidate-001"),
                new EpubCorpusSha256(new string('D', 64))),
            result);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-gate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
