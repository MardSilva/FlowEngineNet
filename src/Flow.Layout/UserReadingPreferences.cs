using Flow.Documents;

namespace Flow.Layout;

public sealed record UserReadingPreferences
{
    public UserReadingPreferences(
        string? preferredBodyFont = null,
        double fontScale = 1,
        string? preferredHeadingFont = null,
        double headingScale = 1,
        double lineHeightScale = 1,
        Length? paragraphSpacing = null,
        Length? contentMargin = null,
        ReadingTheme? theme = null)
    {
        ValidateFontFamily(preferredBodyFont, nameof(preferredBodyFont));
        ValidateFontFamily(preferredHeadingFont, nameof(preferredHeadingFont));
        ValidateScale(fontScale, nameof(fontScale));
        ValidateScale(headingScale, nameof(headingScale));
        ValidateScale(lineHeightScale, nameof(lineHeightScale));
        ValidateNonNegativeLength(paragraphSpacing, nameof(paragraphSpacing));
        ValidateNonNegativeLength(contentMargin, nameof(contentMargin));

        if (theme is not null && !Enum.IsDefined(theme.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "The reading theme is not defined.");
        }

        PreferredBodyFont = preferredBodyFont;
        FontScale = fontScale;
        PreferredHeadingFont = preferredHeadingFont;
        HeadingScale = headingScale;
        LineHeightScale = lineHeightScale;
        ParagraphSpacing = paragraphSpacing;
        ContentMargin = contentMargin;
        Theme = theme;
    }

    public string? PreferredBodyFont { get; }

    public double FontScale { get; }

    public string? PreferredHeadingFont { get; }

    public double HeadingScale { get; }

    public double LineHeightScale { get; }

    public Length? ParagraphSpacing { get; }

    public Length? ContentMargin { get; }

    public ReadingTheme? Theme { get; }

    private static void ValidateScale(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A reading scale must be finite, greater than zero, and no greater than 10.");
        }
    }

    private static void ValidateNonNegativeLength(Length? value, string parameterName)
    {
        if (value is not null && value.Value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The length cannot be negative.");
        }
    }

    private static void ValidateFontFamily(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.IndexOfAny([',', ';', '{', '}']) >= 0
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A preferred font must be one semantic family name.", parameterName);
        }
    }
}
