using Flow.Documents;

namespace Flow.Layout;

public sealed record ResolvedTypographyStyle
{
    public ResolvedTypographyStyle(
        string fontFamily,
        Length fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        double lineHeight,
        Length letterSpacing,
        TextAlignment textAlignment,
        TextTransform textTransform,
        Length marginBefore,
        Length marginAfter,
        Length indent,
        TextDecoration textDecoration = TextDecoration.None)
    {
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

    public string FontFamily { get; }

    public Length FontSize { get; }

    public FontWeight FontWeight { get; }

    public FontStyle FontStyle { get; }

    public double LineHeight { get; }

    public Length LetterSpacing { get; }

    public TextAlignment TextAlignment { get; }

    public TextTransform TextTransform { get; }

    public Length MarginBefore { get; }

    public Length MarginAfter { get; }

    public Length Indent { get; }

    public TextDecoration TextDecoration { get; }

    internal ResolvedTypographyStyle With(
        string? fontFamily = null,
        Length? fontSize = null,
        double? lineHeight = null,
        Length? marginAfter = null) =>
        new(
            fontFamily ?? FontFamily,
            fontSize ?? FontSize,
            FontWeight,
            FontStyle,
            lineHeight ?? LineHeight,
            LetterSpacing,
            TextAlignment,
            TextTransform,
            MarginBefore,
            marginAfter ?? MarginAfter,
            Indent,
            TextDecoration);
}
