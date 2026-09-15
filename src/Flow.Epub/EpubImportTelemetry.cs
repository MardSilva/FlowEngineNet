using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Identifies observable phases of bounded EPUB processing and its immediate outputs.</summary>
public enum EpubImportPhase
{
    CopyingArchive,
    IndexingArchive,
    ReadingContainer,
    ReadingPackage,
    ProcessingManifest,
    ProcessingSpine,
    ConvertingContent,
    FinalizingDocument,
    SerializingDocument,
    AnalyzingFidelity,
    RenderingHtmlBook,
    WritingOutput,
    Completed,
}

/// <summary>Reports one noncanonical, immutable EPUB pipeline progress observation.</summary>
public sealed record EpubImportProgress
{
    public EpubImportProgress(
        EpubImportPhase phase,
        long completedUnits,
        long? totalUnits = null,
        string? currentResource = null)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completedUnits);
        if (totalUnits is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(totalUnits.Value);
            if (completedUnits > totalUnits.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(completedUnits), "Completed units cannot exceed the known total.");
            }
        }

        Phase = phase;
        CompletedUnits = completedUnits;
        TotalUnits = totalUnits;
        CurrentResource = string.IsNullOrWhiteSpace(currentResource) ? null : currentResource;
    }

    public EpubImportPhase Phase { get; }

    public long CompletedUnits { get; }

    public long? TotalUnits { get; }

    public string? CurrentResource { get; }
}

/// <summary>Measures elapsed time for one noncanonical EPUB pipeline phase.</summary>
public sealed record EpubImportPhaseTiming(EpubImportPhase Phase, TimeSpan Duration);

/// <summary>
/// Contains noncanonical measurements for one EPUB import and optional immediate outputs.
/// Timings and managed-memory samples are environment-dependent observations, not limits or benchmark claims.
/// </summary>
public sealed record EpubImportMetrics
{
    internal EpubImportMetrics(
        TimeSpan totalDuration,
        IEnumerable<EpubImportPhaseTiming> phaseTimings,
        int archiveEntryCount,
        long compressedBytes,
        long uncompressedBytes,
        long assetBytes,
        int spineDocumentsProcessed,
        int nodesProduced,
        long charactersProduced,
        long approximatePeakManagedBytes)
    {
        ArgumentNullException.ThrowIfNull(phaseTimings);
        TotalDuration = totalDuration;
        PhaseTimings = phaseTimings.OrderBy(static timing => timing.Phase).ToImmutableArray();
        ArchiveEntryCount = archiveEntryCount;
        CompressedBytes = compressedBytes;
        UncompressedBytes = uncompressedBytes;
        AssetBytes = assetBytes;
        SpineDocumentsProcessed = spineDocumentsProcessed;
        NodesProduced = nodesProduced;
        CharactersProduced = charactersProduced;
        ApproximatePeakManagedBytes = approximatePeakManagedBytes;
    }

    public TimeSpan TotalDuration { get; }

    public ImmutableArray<EpubImportPhaseTiming> PhaseTimings { get; }

    public int ArchiveEntryCount { get; }

    public long CompressedBytes { get; }

    public long UncompressedBytes { get; }

    public long AssetBytes { get; }

    public int SpineDocumentsProcessed { get; }

    public int NodesProduced { get; }

    public long CharactersProduced { get; }

    public long ApproximatePeakManagedBytes { get; }

    public long? FlowJsonBytes { get; init; }

    public int? HtmlFileCount { get; init; }

    public long? HtmlBytes { get; init; }

    /// <summary>Returns a new noncanonical snapshot with an additional measured pipeline phase.</summary>
    public EpubImportMetrics AddPhaseTiming(EpubImportPhase phase, TimeSpan duration)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        var timings = PhaseTimings
            .Where(timing => timing.Phase != phase)
            .Append(new EpubImportPhaseTiming(
                phase,
                duration + PhaseTimings.Where(timing => timing.Phase == phase)
                    .Aggregate(TimeSpan.Zero, static (total, timing) => total + timing.Duration)))
            .OrderBy(static timing => timing.Phase)
            .ToArray();
        return new EpubImportMetrics(
            TotalDuration + duration,
            timings,
            ArchiveEntryCount,
            CompressedBytes,
            UncompressedBytes,
            AssetBytes,
            SpineDocumentsProcessed,
            NodesProduced,
            CharactersProduced,
            ApproximatePeakManagedBytes)
        {
            FlowJsonBytes = FlowJsonBytes,
            HtmlFileCount = HtmlFileCount,
            HtmlBytes = HtmlBytes,
        };
    }

    /// <summary>Returns a new snapshot with measured immediate output sizes.</summary>
    public EpubImportMetrics WithOutputSizes(long? flowJsonBytes, int? htmlFileCount, long? htmlBytes)
    {
        if (flowJsonBytes < 0 || htmlFileCount < 0 || htmlBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flowJsonBytes), "Output sizes cannot be negative.");
        }

        return this with
        {
            FlowJsonBytes = flowJsonBytes,
            HtmlFileCount = htmlFileCount,
            HtmlBytes = htmlBytes,
        };
    }
}
