namespace Flow.Documents;

public sealed record TypographyStyle
{
    public TypographyStyle(
        string? fontFamily = null,
        Length? fontSize = null,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null,
        double? lineHeight = null,
        Length? letterSpacing = null,
        TextAlignment? textAlignment = null,
        TextTransform? textTransform = null,
        Length? marginBefore = null,
        Length? marginAfter = null,
        Length? indent = null,
        TextDecoration? textDecoration = null)
    {
        ValidateFontFamily(fontFamily);
        ValidatePositiveLength(fontSize, nameof(fontSize));
        ValidateEnum(fontWeight, nameof(fontWeight));
        ValidateEnum(fontStyle, nameof(fontStyle));
        ValidateEnum(textAlignment, nameof(textAlignment));
        ValidateEnum(textTransform, nameof(textTransform));
        ValidateTextDecoration(textDecoration);

        if (lineHeight is not null && (!double.IsFinite(lineHeight.Value) || lineHeight.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeight), lineHeight, "Line height must be finite and greater than zero.");
        }

        FontFamily = fontFamily;
        FontSize = fontSize;
        FontWeight = fontWeight;
        FontStyle = fontStyle;
        LineHeight = lineHeight;
        LetterSpacing = letterSpacing;
        TextAlignment = textAlignment;
        TextTransform = textTransform;
        MarginBefore = marginBefore;
        MarginAfter = marginAfter;
        Indent = indent;
        TextDecoration = textDecoration;
    }

    public string? FontFamily { get; }

    public Length? FontSize { get; }

    public FontWeight? FontWeight { get; }

    public FontStyle? FontStyle { get; }

    public double? LineHeight { get; }

    public Length? LetterSpacing { get; }

    public TextAlignment? TextAlignment { get; }

    public TextTransform? TextTransform { get; }

    public Length? MarginBefore { get; }

    public Length? MarginAfter { get; }

    public Length? Indent { get; }

    public TextDecoration? TextDecoration { get; }

    public bool IsEmpty => FontFamily is null
        && FontSize is null
        && FontWeight is null
        && FontStyle is null
        && LineHeight is null
        && LetterSpacing is null
        && TextAlignment is null
        && TextTransform is null
        && MarginBefore is null
        && MarginAfter is null
        && Indent is null
        && TextDecoration is null;

    private static void ValidateTextDecoration(TextDecoration? value)
    {
        const Flow.Documents.TextDecoration supported =
            Flow.Documents.TextDecoration.Underline | Flow.Documents.TextDecoration.LineThrough;
        if (value is not null && (value.Value & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The text decoration contains unsupported flags.");
        }
    }

    private static void ValidateFontFamily(string? fontFamily)
    {
        if (fontFamily is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            throw new ArgumentException("A font family cannot be empty or whitespace.", nameof(fontFamily));
        }

        if (!string.Equals(fontFamily, fontFamily.Trim(), StringComparison.Ordinal)
            || fontFamily.IndexOfAny([',', ';', '{', '}']) >= 0
            || fontFamily.Any(char.IsControl))
        {
            throw new ArgumentException("A font family must be one semantic family name, not a CSS expression.", nameof(fontFamily));
        }
    }

    private static void ValidatePositiveLength(Length? length, string parameterName)
    {
        if (length is not null && length.Value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, length, "The length must be greater than zero.");
        }
    }

    private static void ValidateEnum<TEnum>(TEnum? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (value is not null && !Enum.IsDefined(value.Value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value is not defined.");
        }
    }
}
