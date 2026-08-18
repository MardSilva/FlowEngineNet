using System.Diagnostics;
using Flow.Documents;

namespace Flow.Epub;

internal sealed class EpubImportTelemetryCollector
{
    private readonly IProgress<EpubImportProgress>? progress;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Dictionary<EpubImportPhase, TimeSpan> durations = [];
    private EpubImportPhase? currentPhase;
    private long phaseStarted;
    private long lastCompleted;
    private long approximatePeakManagedBytes;

    internal EpubImportTelemetryCollector(IProgress<EpubImportProgress>? progress)
    {
        this.progress = progress;
        SampleMemory();
    }

    internal int ArchiveEntryCount { get; set; }

    internal long CompressedBytes { get; set; }

    internal long UncompressedBytes { get; set; }

    internal int SpineDocumentsProcessed { get; set; }

    internal void Start(EpubImportPhase phase, long? totalUnits = null, string? resource = null)
    {
        CompleteCurrentPhase();
        currentPhase = phase;
        phaseStarted = Stopwatch.GetTimestamp();
        lastCompleted = 0;
        SampleMemory();
        Report(0, totalUnits, resource);
    }

    internal void Advance(long completedUnits, long? totalUnits = null, string? resource = null)
    {
        if (currentPhase is null)
        {
            throw new InvalidOperationException("A progress phase must be started before it can advance.");
        }

        var monotonic = Math.Max(lastCompleted, completedUnits);
        if (totalUnits is not null)
        {
            monotonic = Math.Min(monotonic, totalUnits.Value);
        }

        lastCompleted = monotonic;
        SampleMemory();
        Report(monotonic, totalUnits, resource);
    }

    internal EpubImportMetrics Finish(FlowDocument? document)
    {
        CompleteCurrentPhase();
        SampleMemory();
        var assetBytes = document?.Assets.Values.Sum(static asset => (long)asset.Data.Length) ?? 0;
        return new EpubImportMetrics(
            Stopwatch.GetElapsedTime(started),
            durations.Select(static pair => new EpubImportPhaseTiming(pair.Key, pair.Value)),
            ArchiveEntryCount,
            CompressedBytes,
            UncompressedBytes,
            assetBytes,
            SpineDocumentsProcessed,
            document?.Index.NodeCount ?? 0,
            document is null ? 0 : EpubProducedContentMetrics.CountCharacters(document),
            approximatePeakManagedBytes);
    }

    private void CompleteCurrentPhase()
    {
        if (currentPhase is null)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(phaseStarted);
        durations[currentPhase.Value] = durations.GetValueOrDefault(currentPhase.Value) + elapsed;
        currentPhase = null;
    }

    private void Report(long completedUnits, long? totalUnits, string? resource)
    {
        if (progress is null || currentPhase is null)
        {
            return;
        }

        try
        {
            progress.Report(new EpubImportProgress(currentPhase.Value, completedUnits, totalUnits, resource));
        }
        catch (Exception)
        {
            // Host observers are deliberately isolated from import identity and results.
        }
    }

    private void SampleMemory()
    {
        var sample = GC.GetTotalMemory(forceFullCollection: false);
        approximatePeakManagedBytes = Math.Max(approximatePeakManagedBytes, sample);
    }
}
