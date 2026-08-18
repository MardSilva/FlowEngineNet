using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Preserves typed OPF source metadata that is not part of the canonical Flow document.</summary>
public sealed record EpubMetadataReport
{
    public EpubMetadataReport(
        string? uniqueIdentifierId,
        string? selectedIdentifier,
        IEnumerable<EpubIdentifierMetadata>? identifiers = null,
        IEnumerable<EpubTitleMetadata>? titles = null,
        IEnumerable<EpubAgentMetadata>? creators = null,
        IEnumerable<EpubAgentMetadata>? contributors = null,
        IEnumerable<EpubMetadataValue>? publishers = null,
        IEnumerable<EpubMetadataValue>? languages = null,
        IEnumerable<EpubMetadataValue>? descriptions = null,
        IEnumerable<EpubMetadataValue>? subjects = null,
        IEnumerable<EpubDateMetadata>? dates = null,
        IEnumerable<EpubMetadataValue>? rights = null,
        string? modified = null,
        EpubCoverMetadata? cover = null,
        EpubAccessibilityMetadata? accessibility = null,
        IEnumerable<EpubMetadataProperty>? properties = null)
    {
        UniqueIdentifierId = uniqueIdentifierId;
        SelectedIdentifier = selectedIdentifier;
        Identifiers = Copy(identifiers);
        Titles = Copy(titles);
        Creators = Copy(creators);
        Contributors = Copy(contributors);
        Publishers = Copy(publishers);
        Languages = Copy(languages);
        Descriptions = Copy(descriptions);
        Subjects = Copy(subjects);
        Dates = Copy(dates);
        Rights = Copy(rights);
        Modified = modified;
        Cover = cover;
        Accessibility = accessibility ?? new EpubAccessibilityMetadata();
        Properties = Copy(properties);
    }

    public string? UniqueIdentifierId { get; }

    public string? SelectedIdentifier { get; }

    public ImmutableArray<EpubIdentifierMetadata> Identifiers { get; }

    public ImmutableArray<EpubTitleMetadata> Titles { get; }

    public ImmutableArray<EpubAgentMetadata> Creators { get; }

    public ImmutableArray<EpubAgentMetadata> Contributors { get; }

    public ImmutableArray<EpubMetadataValue> Publishers { get; }

    public ImmutableArray<EpubMetadataValue> Languages { get; }

    public ImmutableArray<EpubMetadataValue> Descriptions { get; }

    public ImmutableArray<EpubMetadataValue> Subjects { get; }

    public ImmutableArray<EpubDateMetadata> Dates { get; }

    public ImmutableArray<EpubMetadataValue> Rights { get; }

    public string? Modified { get; }

    public EpubCoverMetadata? Cover { get; }

    public EpubAccessibilityMetadata Accessibility { get; }

    public ImmutableArray<EpubMetadataProperty> Properties { get; }

    internal EpubMetadataReport WithCover(EpubCoverMetadata? cover) => new(
        UniqueIdentifierId,
        SelectedIdentifier,
        Identifiers,
        Titles,
        Creators,
        Contributors,
        Publishers,
        Languages,
        Descriptions,
        Subjects,
        Dates,
        Rights,
        Modified,
        cover,
        Accessibility,
        Properties);

    private static ImmutableArray<T> Copy<T>(IEnumerable<T>? values) =>
        values?.ToImmutableArray() ?? [];
}

/// <summary>Describes one OPF identifier and whether the package selected it as unique.</summary>
public sealed record EpubIdentifierMetadata(string Value, string? Id, string? Scheme, bool IsUnique);

/// <summary>Describes one OPF title with resolved EPUB 3 refinements.</summary>
public sealed record EpubTitleMetadata(
    string Value,
    string? Id,
    string? TitleType,
    int? DisplaySequence,
    string? FileAs,
    string? Language);

/// <summary>Distinguishes creator and contributor source elements.</summary>
public enum EpubAgentKind
{
    Creator,
    Contributor,
}

/// <summary>Describes an OPF creator or contributor and its resolved roles.</summary>
public sealed record EpubAgentMetadata(
    string Value,
    string? Id,
    EpubAgentKind Kind,
    ImmutableArray<string> Roles,
    string? FileAs,
    string? Language);

/// <summary>Preserves one textual OPF metadata value.</summary>
public sealed record EpubMetadataValue(string Value, string? Id, string? Language);

/// <summary>Preserves one OPF date and whether it passed supported ISO-8601 validation.</summary>
public sealed record EpubDateMetadata(string Value, string? Event, bool IsValid);

/// <summary>Preserves one EPUB 3 meta property and its source relationship.</summary>
public sealed record EpubMetadataProperty(
    string Property,
    string Value,
    string? Refines,
    string? Scheme,
    string? Language);

/// <summary>Describes the manifest resource selected as the publication cover.</summary>
public sealed record EpubCoverMetadata(string ItemId, string Path, string MediaType, string Source);

/// <summary>Collects recognized schema.org accessibility metadata without making it canonical.</summary>
public sealed record EpubAccessibilityMetadata
{
    public EpubAccessibilityMetadata(
        IEnumerable<string>? accessModes = null,
        IEnumerable<string>? accessModeSufficient = null,
        IEnumerable<string>? features = null,
        IEnumerable<string>? hazards = null,
        IEnumerable<string>? summaries = null)
    {
        AccessModes = Copy(accessModes);
        AccessModeSufficient = Copy(accessModeSufficient);
        Features = Copy(features);
        Hazards = Copy(hazards);
        Summaries = Copy(summaries);
    }

    public ImmutableArray<string> AccessModes { get; }

    public ImmutableArray<string> AccessModeSufficient { get; }

    public ImmutableArray<string> Features { get; }

    public ImmutableArray<string> Hazards { get; }

    public ImmutableArray<string> Summaries { get; }

    private static ImmutableArray<string> Copy(IEnumerable<string>? values) =>
        values?.ToImmutableArray() ?? [];
}
