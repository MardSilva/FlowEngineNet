using Flow.Documents;

namespace Flow.Layout;

public static class FlowStyleDefaults
{
    public static ResolvedReadingStyle Create()
    {
        var styles = Enum.GetValues<TypographyRole>()
            .Select(static role => KeyValuePair.Create(role, CreateStyle(role)));

        return new ResolvedReadingStyle(
            new ResolvedTypographySet(styles),
            Length.Rem(1.5),
            ReadingTheme.System);
    }

    private static ResolvedTypographyStyle CreateStyle(TypographyRole role)
    {
        var isHeading = IsHeadingRole(role);
        var fontFamily = role == TypographyRole.Code
            ? "System Monospace"
            : isHeading
                ? "System Sans Serif"
                : "System Serif";
        var fontSize = Length.Px(GetFontSize(role));
        var fontWeight = isHeading || role == TypographyRole.TableOfContentsLevel1
            ? FontWeight.Bold
            : FontWeight.Normal;
        var lineHeight = role is TypographyRole.Caption or TypographyRole.Footnote or TypographyRole.Code
            ? 1.35
            : 1.5;
        var marginBefore = isHeading ? Length.Rem(1.5) : Length.Px(0);
        var marginAfter = role switch
        {
            TypographyRole.Body => Length.Rem(1),
            TypographyRole.ChapterTitle => Length.Rem(1.5),
            _ when isHeading => Length.Rem(0.75),
            _ => Length.Px(0),
        };

        return new ResolvedTypographyStyle(
            fontFamily,
            fontSize,
            fontWeight,
            FontStyle.Normal,
            lineHeight,
            Length.Em(0),
            TextAlignment.Start,
            TextTransform.None,
            marginBefore,
            marginAfter,
            Length.Px(0),
            TextDecoration.None);
    }

    private static double GetFontSize(TypographyRole role) => role switch
    {
        TypographyRole.Body => 18,
        TypographyRole.ChapterTitle => 36,
        TypographyRole.Heading1 => 32,
        TypographyRole.Heading2 => 28,
        TypographyRole.Heading3 => 24,
        TypographyRole.Heading4 => 22,
        TypographyRole.Heading5 => 20,
        TypographyRole.Heading6 => 18,
        TypographyRole.Subtitle => 22,
        TypographyRole.TableOfContentsTitle => 28,
        TypographyRole.TableOfContentsLevel1 => 18,
        TypographyRole.TableOfContentsLevel2 => 17,
        TypographyRole.TableOfContentsLevel3 => 16,
        TypographyRole.Caption => 14,
        TypographyRole.Footnote => 13,
        TypographyRole.BlockQuote => 18,
        TypographyRole.Code => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown typography role."),
    };

    internal static bool IsHeadingRole(TypographyRole role) => role is
        TypographyRole.ChapterTitle
        or TypographyRole.Heading1
        or TypographyRole.Heading2
        or TypographyRole.Heading3
        or TypographyRole.Heading4
        or TypographyRole.Heading5
        or TypographyRole.Heading6
        or TypographyRole.TableOfContentsTitle;

    internal static bool IsBodyFontRole(TypographyRole role) => role is
        TypographyRole.Body
        or TypographyRole.Subtitle
        or TypographyRole.TableOfContentsLevel1
        or TypographyRole.TableOfContentsLevel2
        or TypographyRole.TableOfContentsLevel3
        or TypographyRole.Caption
        or TypographyRole.Footnote
        or TypographyRole.BlockQuote;
}
