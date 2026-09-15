namespace Flow.Documents;

/// <summary>Represents a structurally valid, canonically cased BCP 47 language tag.</summary>
public sealed record LanguageTag
{
    public LanguageTag(string value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException($"Language tag '{value}' is not a structurally valid BCP 47 tag.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static LanguageTag Parse(string value) => new(value);

    public static bool TryParse(
        string? value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LanguageTag? languageTag)
    {
        if (!TryNormalize(value, out var normalized))
        {
            languageTag = null;
            return false;
        }

        languageTag = new LanguageTag(normalized);
        return true;
    }

    public override string ToString() => Value;

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split('-');
        if (parts.Length == 0
            || parts[0].Length == 1
            && !string.Equals(parts[0], "x", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parts[0], "i", StringComparison.OrdinalIgnoreCase)
            || parts[0].Length is < 1 or > 8
            || !parts[0].All(IsAsciiLetter)
            || parts[0].Length == 1 && parts.Length == 1
            || parts.Skip(1).Any(static part => part.Length is < 1 or > 8 || !part.All(IsAsciiLetterOrDigit)))
        {
            return false;
        }

        var normalizedParts = new string[parts.Length];
        normalizedParts[0] = parts[0].ToLowerInvariant();
        for (var index = 1; index < parts.Length; index++)
        {
            var part = parts[index];
            normalizedParts[index] = part.Length == 4 && part.All(IsAsciiLetter)
                ? char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
                : part.Length == 2 && part.All(IsAsciiLetter)
                    ? part.ToUpperInvariant()
                    : part.ToLowerInvariant();
        }

        normalized = string.Join('-', normalizedParts);
        return true;
    }

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char value) => IsAsciiLetter(value) || value is >= '0' and <= '9';
}

/// <summary>Declares the natural language of an inline semantic range.</summary>
public sealed record LanguageSpan : InlineContainerNode
{
    public LanguageSpan(LanguageTag language, IEnumerable<InlineNode> children)
        : base(children)
    {
        ArgumentNullException.ThrowIfNull(language);
        Language = language;
    }

    public LanguageTag Language { get; }
}

public enum TextDirection
{
    Auto,
    LeftToRight,
    RightToLeft,
}

public enum BidirectionalMode
{
    Embedding,
    Isolation,
    Override,
}

/// <summary>Declares Unicode bidirectional intent for an inline semantic range.</summary>
public sealed record BidirectionalSpan : InlineContainerNode
{
    public BidirectionalSpan(
        TextDirection direction,
        BidirectionalMode mode,
        IEnumerable<InlineNode> children)
        : base(children)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == BidirectionalMode.Override && direction == TextDirection.Auto)
        {
            throw new ArgumentException("A bidirectional override requires an explicit left-to-right or right-to-left direction.", nameof(direction));
        }

        Direction = direction;
        Mode = mode;
    }

    public TextDirection Direction { get; }

    public BidirectionalMode Mode { get; }
}

/// <summary>Contains ruby base content, annotations, and optional fallback parentheses in source order.</summary>
public sealed record Ruby : InlineContainerNode
{
    public Ruby(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

/// <summary>Represents the pronunciation or annotation portion of ruby content.</summary>
public sealed record RubyAnnotation : InlineContainerNode
{
    public RubyAnnotation(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

/// <summary>Represents fallback punctuation around a ruby annotation.</summary>
public sealed record RubyFallbackParenthesis : InlineContainerNode
{
    public RubyFallbackParenthesis(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}
