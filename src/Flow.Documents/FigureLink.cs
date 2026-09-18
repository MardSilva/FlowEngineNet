using System.Diagnostics.CodeAnalysis;
using Flow.Core;

namespace Flow.Documents;

/// <summary>Represents a safe semantic destination associated with a figure.</summary>
public sealed record FigureLink
{
    private static readonly HashSet<string> AllowedExternalSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "mailto",
    };

    private FigureLink(DocumentAnchor? anchor, string? externalUri)
    {
        Anchor = anchor;
        ExternalUri = externalUri;
    }

    /// <summary>Gets the internal document destination, when this is an internal link.</summary>
    public DocumentAnchor? Anchor { get; }

    /// <summary>Gets the safe absolute external URI, when this is an external link.</summary>
    public string? ExternalUri { get; }

    /// <summary>Gets whether this link targets the current document.</summary>
    public bool IsInternal => Anchor is not null;

    /// <summary>Creates a figure link to a stable document anchor.</summary>
    public static FigureLink Internal(DocumentAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return new FigureLink(anchor, null);
    }

    /// <summary>Creates a figure link to a safe absolute external URI.</summary>
    public static FigureLink External(string uri)
    {
        if (!TryCreateExternal(uri, out var link))
        {
            throw new ArgumentException("The figure link must be a safe absolute HTTP, HTTPS, or mailto URI.", nameof(uri));
        }

        return link;
    }

    /// <summary>Attempts to create a safe external figure link.</summary>
    public static bool TryCreateExternal(string? uri, [NotNullWhen(true)] out FigureLink? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(uri)
            || uri.Any(static character => char.IsControl(character))
            || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !AllowedExternalSchemes.Contains(parsed.Scheme))
        {
            return false;
        }

        try
        {
            if (Uri.UnescapeDataString(uri).Any(char.IsControl))
            {
                return false;
            }
        }
        catch (UriFormatException)
        {
            return false;
        }

        link = new FigureLink(null, uri);
        return true;
    }

    /// <summary>Returns the canonical target used by renderers.</summary>
    public string ToTargetString() => Anchor?.Value ?? ExternalUri!;
}
