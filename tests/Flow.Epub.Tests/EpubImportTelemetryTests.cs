using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubImportTelemetryTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task Import_ReportsMonotonicProgressAndCompleteNoncanonicalMetrics()
    {
        await using var epub = MinimalEpubFactory.Create();
        var observations = new List<EpubImportProgress>();

        var result = await new EpubImporter().ImportAsync(epub, new CallbackProgress(observations.Add));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotEmpty(observations);
        Assert.Equal(EpubImportPhase.CopyingArchive, observations[0].Phase);
        Assert.Equal(EpubImportPhase.Completed, observations[^1].Phase);
        foreach (var phase in observations.GroupBy(static item => item.Phase))
        {
            var known = phase.Where(static item => item.TotalUnits is not null).ToArray();
            Assert.Equal(
                known.Select(static item => item.CompletedUnits).Order().ToArray(),
                known.Select(static item => item.CompletedUnits).ToArray());
            Assert.All(known, static item => Assert.InRange(item.CompletedUnits, 0, item.TotalUnits!.Value));
        }

        var metrics = Assert.IsType<EpubImportMetrics>(result.Metrics);
        Assert.True(metrics.TotalDuration >= TimeSpan.Zero);
        Assert.True(metrics.ArchiveEntryCount >= 6);
        Assert.True(metrics.CompressedBytes > 0);
        Assert.True(metrics.UncompressedBytes > 0);
        Assert.True(metrics.AssetBytes > 0);
        Assert.Equal(2, metrics.SpineDocumentsProcessed);
        Assert.Equal(result.Document!.Index.NodeCount, metrics.NodesProduced);
        Assert.True(metrics.CharactersProduced > 0);
        Assert.True(metrics.ApproximatePeakManagedBytes > 0);
        Assert.Contains(metrics.PhaseTimings, static timing => timing.Phase == EpubImportPhase.ProcessingSpine);
        Assert.Null(metrics.FlowJsonBytes);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task ThrowingProgressObserverCannotChangeImportedBytes()
    {
        await using var baselineSource = MinimalEpubFactory.Create();
        await using var observedSource = MinimalEpubFactory.Create();
        var importer = new EpubImporter();

        var baseline = await importer.ImportAsync(baselineSource);
        var observed = await importer.ImportAsync(
            observedSource,
            new CallbackProgress(static _ => throw new InvalidOperationException("host observer failure")));

        Assert.True(baseline.IsSuccess);
        Assert.True(observed.IsSuccess);
        Assert.Equal(await SerializeAsync(baseline.Document!), await SerializeAsync(observed.Document!));
        Assert.Equal(
            baseline.Diagnostics.Select(static diagnostic => diagnostic.Code),
            observed.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ImportCancellationDuringSpineStopsWithoutInvalidArchiveDiagnostic()
    {
        await using var epub = MinimalEpubFactory.Create();
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(item =>
        {
            if (item.Phase == EpubImportPhase.ProcessingSpine && item.CompletedUnits == 1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EpubImporter().ImportAsync(epub, progress, cancellation.Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task InspectionCancellationDuringArchiveCopyIsNotReportedAsInvalidInput()
    {
        await using var epub = MinimalEpubFactory.Create();
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress(item =>
        {
            if (item.Phase == EpubImportPhase.CopyingArchive && item.CompletedUnits > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EpubPublicationInspector().InspectAsync(epub, progress, cancellation.Token));
    }

    [Fact]
    public void ProgressRejectsImpossibleKnownTotals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EpubImportProgress(EpubImportPhase.ProcessingSpine, 2, 1));
    }

    private static async Task<byte[]> SerializeAsync(FlowDocument document)
    {
        await using var destination = new MemoryStream();
        await new FlowJsonDocumentSerializer().SerializeAsync(document, destination);
        return destination.ToArray();
    }

    private sealed class CallbackProgress(Action<EpubImportProgress> callback) : IProgress<EpubImportProgress>
    {
        public void Report(EpubImportProgress value) => callback(value);
    }
}
