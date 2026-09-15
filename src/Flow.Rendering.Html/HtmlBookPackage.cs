using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Flow.Rendering.Html;

/// <summary>Identifies the canonical document represented by an HTML book package.</summary>
public sealed record HtmlBookIntegrity
{
    public HtmlBookIntegrity(string algorithm, string hash, string canonicalizationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalizationVersion);
        Algorithm = algorithm;
        Hash = hash;
        CanonicalizationVersion = canonicalizationVersion;
    }

    public string Algorithm { get; }

    public string Hash { get; }

    public string CanonicalizationVersion { get; }
}

/// <summary>Contains one immutable, safely addressed file in an HTML book package.</summary>
public sealed record HtmlBookFile
{
    public HtmlBookFile(string path, string mediaType, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        Path = NormalizeAndValidatePath(path);
        MediaType = mediaType;
        Content = ImmutableArray.CreateRange(content.ToArray());
    }

    private HtmlBookFile(string path, string mediaType, ImmutableArray<byte> content)
    {
        Path = NormalizeAndValidatePath(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        MediaType = mediaType;
        Content = content;
    }

    public string Path { get; }

    public string MediaType { get; }

    public ImmutableArray<byte> Content { get; }

    internal static HtmlBookFile FromOwnedBytes(string path, string mediaType, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new HtmlBookFile(path, mediaType, ImmutableCollectionsMarshal.AsImmutableArray(content));
    }

    internal static string NormalizeAndValidatePath(string path)
    {
        if (path.Contains('\\')
            || path.StartsWith("/", StringComparison.Ordinal)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A package path must be a relative forward-slash path.", nameof(path));
        }

        var segments = path.Split('/');
        if (segments.Any(static segment => segment.Length == 0
                                           || segment is "." or ".."
                                           || segment.Any(static character =>
                                               !char.IsAsciiLetterOrDigit(character)
                                               && character is not '-' and not '_' and not '.')))
        {
            throw new ArgumentException("A package path cannot contain empty, dot, traversal, or nonportable segments.", nameof(path));
        }

        return string.Join('/', segments);
    }
}

/// <summary>Contains every deterministic file produced for a navigable HTML book.</summary>
public sealed record HtmlBookPackage
{
    public const string Format = "flow-html-book-0.1";

    public HtmlBookPackage(IEnumerable<HtmlBookFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var materialized = files.OrderBy(static file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        if (materialized.Any(static file => file is null))
        {
            throw new ArgumentException("Package files cannot contain null values.", nameof(files));
        }

        var duplicate = materialized
            .GroupBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Package path '{duplicate.Key}' occurs more than once.", nameof(files));
        }

        foreach (var required in new[] { "index.html", "toc.html", "styles/book.css", "manifest.json" })
        {
            if (!materialized.Any(file => string.Equals(file.Path, required, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Required package file '{required}' is missing.", nameof(files));
            }
        }

        Files = materialized;
    }

    public ImmutableArray<HtmlBookFile> Files { get; }

    public HtmlBookFile GetFile(string path)
    {
        var normalized = HtmlBookFile.NormalizeAndValidatePath(path);
        return Files.FirstOrDefault(file => string.Equals(file.Path, normalized, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException($"Package file '{normalized}' was not found.");
    }
}
