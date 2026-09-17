using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Classifies how a publication enters the EPUB interoperability corpus.</summary>
public enum EpubCorpusPublicationKind
{
    ProjectFixture,
    RedistributablePublication,
    LocalNonRedistributablePublication,
}

/// <summary>States whether the publication bytes may be redistributed.</summary>
public enum EpubCorpusRedistribution
{
    Allowed,
    Prohibited,
}

/// <summary>Identifies a corpus publication independently of its file name.</summary>
public readonly record struct EpubCorpusPublicationId
{
    public const int MaximumLength = 64;

    public EpubCorpusPublicationId(string value)
    {
        if (!TryParse(value, out var parsed))
        {
            throw new ArgumentException(
                "Corpus publication IDs must contain 1 to 64 lowercase ASCII letters, digits, dots, hyphens, or underscores and must start with a letter or digit.",
                nameof(value));
        }

        Value = parsed.Value;
    }

    private EpubCorpusPublicationId(string value, bool _)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out EpubCorpusPublicationId result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength || !IsLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        result = new EpubCorpusPublicationId(value, true);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    private static bool IsLetterOrDigit(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>Contains an uppercase SHA-256 value used to identify corpus input bytes.</summary>
public readonly record struct EpubCorpusSha256
{
    public EpubCorpusSha256(string value)
    {
        if (!TryParse(value, out var parsed))
        {
            throw new ArgumentException("A corpus SHA-256 value must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        Value = parsed.Value;
    }

    private EpubCorpusSha256(string value, bool _)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out EpubCorpusSha256 result)
    {
        result = default;
        if (value is null || value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        result = new EpubCorpusSha256(value.ToUpperInvariant(), true);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Describes the license assertion recorded for one corpus publication.</summary>
public sealed record EpubCorpusLicense
{
    public EpubCorpusLicense(string name, string evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        Name = name;
        Evidence = evidence;
    }

    public string Name { get; }

    public string Evidence { get; }
}

/// <summary>Describes one expected EPUB input without storing its bytes in a Flow document.</summary>
public sealed record EpubCorpusPublication
{
    public EpubCorpusPublication(
        EpubCorpusPublicationId id,
        string title,
        string origin,
        EpubCorpusLicense license,
        EpubCorpusPublicationKind kind,
        EpubCorpusRedistribution redistribution,
        string relativePath,
        EpubVersionFamily expectedEpubVersion,
        long expectedSizeBytes,
        IEnumerable<string> languages,
        IEnumerable<string> expectedResources,
        IEnumerable<string> expectedFeatures,
        IEnumerable<string> expectedResults,
        IEnumerable<string> knownLimitations,
        EpubCorpusSha256 sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ArgumentNullException.ThrowIfNull(license);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(expectedResources);
        ArgumentNullException.ThrowIfNull(expectedFeatures);
        ArgumentNullException.ThrowIfNull(expectedResults);
        ArgumentNullException.ThrowIfNull(knownLimitations);

        Id = id;
        Title = title;
        Origin = origin;
        License = license;
        Kind = kind;
        Redistribution = redistribution;
        RelativePath = relativePath;
        ExpectedEpubVersion = expectedEpubVersion;
        ExpectedSizeBytes = expectedSizeBytes;
        Languages = languages.ToImmutableArray();
        ExpectedResources = ToOrderedSet(expectedResources);
        ExpectedFeatures = ToOrderedSet(expectedFeatures);
        ExpectedResults = ToOrderedSet(expectedResults);
        KnownLimitations = ToOrderedSet(knownLimitations);
        Sha256 = sha256;
    }

    public EpubCorpusPublicationId Id { get; }

    public string Title { get; }

    public string Origin { get; }

    public EpubCorpusLicense License { get; }

    public EpubCorpusPublicationKind Kind { get; }

    public EpubCorpusRedistribution Redistribution { get; }

    public string RelativePath { get; }

    public EpubVersionFamily ExpectedEpubVersion { get; }

    public long ExpectedSizeBytes { get; }

    public ImmutableArray<string> Languages { get; }

    public ImmutableArray<string> ExpectedResources { get; }

    public ImmutableArray<string> ExpectedFeatures { get; }

    public ImmutableArray<string> ExpectedResults { get; }

    public ImmutableArray<string> KnownLimitations { get; }

    public EpubCorpusSha256 Sha256 { get; }

    private static ImmutableArray<string> ToOrderedSet(IEnumerable<string> values) => values
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToImmutableArray();
}

/// <summary>Contains the versioned, deterministically ordered EPUB corpus catalog.</summary>
public sealed record EpubCorpusManifest
{
    public const string CurrentFormat = "flow-epub-corpus-0.1";

    public EpubCorpusManifest(string format, IEnumerable<EpubCorpusPublication> publications)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(publications);
        Format = format;
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public string Format { get; }

    public ImmutableArray<EpubCorpusPublication> Publications { get; }
}

/// <summary>Classifies a corpus-manifest validation finding.</summary>
public enum EpubCorpusDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>Describes one deterministic corpus-manifest validation finding.</summary>
public sealed record EpubCorpusDiagnostic(
    string Code,
    EpubCorpusDiagnosticSeverity Severity,
    string Message,
    string? Resource = null);

/// <summary>Defines stable diagnostic codes for the EPUB corpus manifest.</summary>
public static class EpubCorpusDiagnosticCodes
{
    public const string InvalidJson = "EPC001";
    public const string InvalidEncoding = "EPC002";
    public const string UnsupportedFormat = "EPC003";
    public const string MissingRequiredValue = "EPC004";
    public const string DuplicatePublicationId = "EPC005";
    public const string InvalidPublicationId = "EPC006";
    public const string InvalidSha256 = "EPC007";
    public const string UnsupportedEpubVersion = "EPC008";
    public const string UnsafeRelativePath = "EPC009";
    public const string InvalidLicense = "EPC010";
    public const string InvalidValue = "EPC011";
    public const string UnknownProperty = "EPC012";
}

/// <summary>Contains either a valid corpus manifest or deterministic diagnostics.</summary>
public sealed record EpubCorpusManifestReadResult
{
    public EpubCorpusManifestReadResult(
        EpubCorpusManifest? manifest,
        IEnumerable<EpubCorpusDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Manifest = manifest;
        Diagnostics = diagnostics
            .OrderBy(static item => item.Resource, StringComparer.Ordinal)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public EpubCorpusManifest? Manifest { get; }

    public ImmutableArray<EpubCorpusDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Manifest is not null
        && Diagnostics.All(static item => item.Severity != EpubCorpusDiagnosticSeverity.Error);
}
