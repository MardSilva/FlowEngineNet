using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Identifies the EPUB specification family declared by a package document.</summary>
public enum EpubVersionFamily
{
    Unknown,
    Epub2,
    Epub3,
}

/// <summary>Describes the package document and its principal metadata without importing content.</summary>
public sealed record EpubPackageInfo
{
    public EpubPackageInfo(
        string path,
        string? declaredVersion,
        EpubVersionFamily versionFamily,
        string? uniqueIdentifierId,
        string? identifier,
        string? title,
        string? language,
        IEnumerable<string> creators,
        string? publisher = null,
        string? description = null,
        string? modified = null,
        IEnumerable<string>? languages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(creators);

        Path = path;
        DeclaredVersion = declaredVersion;
        VersionFamily = versionFamily;
        UniqueIdentifierId = uniqueIdentifierId;
        Identifier = identifier;
        Title = title;
        Language = language;
        Creators = creators.ToImmutableArray();
        Publisher = publisher;
        Description = description;
        Modified = modified;
        Languages = (languages ?? (language is null ? [] : [language]))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public string Path { get; }

    public string? DeclaredVersion { get; }

    public EpubVersionFamily VersionFamily { get; }

    public string? UniqueIdentifierId { get; }

    public string? Identifier { get; }

    public string? Title { get; }

    public string? Language { get; }

    /// <summary>Gets every non-empty language declared by the package in deterministic order.</summary>
    public ImmutableArray<string> Languages { get; }

    public ImmutableArray<string> Creators { get; }

    public string? Publisher { get; }

    public string? Description { get; }

    public string? Modified { get; }
}
