namespace Flow.Epub;

/// <summary>Describes one OPF spine reference in declared reading order.</summary>
public sealed record EpubSpineItemInfo(
    int Position,
    string? IdRef,
    bool IsLinear,
    string? ResourcePath,
    string? MediaType,
    bool ExistsInArchive,
    bool IsSupported);
