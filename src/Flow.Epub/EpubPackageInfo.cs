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
        string? modified = null)
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
    }

    public string Path { get; }

    public string? DeclaredVersion { get; }

    public EpubVersionFamily VersionFamily { get; }

    public string? UniqueIdentifierId { get; }

    public string? Identifier { get; }

    public string? Title { get; }

    public string? Language { get; }

    public ImmutableArray<string> Creators { get; }

    public string? Publisher { get; }

    public string? Description { get; }

    public string? Modified { get; }
}
