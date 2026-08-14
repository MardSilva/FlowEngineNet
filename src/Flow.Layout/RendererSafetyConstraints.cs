using Flow.Documents;

namespace Flow.Layout;

public sealed record RendererSafetyConstraints
{
    public RendererSafetyConstraints(
        Length? minimumFontSize = null,
        Length? maximumFontSize = null,
        double minimumLineHeight = 1,
        double maximumLineHeight = 3,
        Length? minimumParagraphSpacing = null,
        Length? minimumContentMargin = null,
        string? requiredFontFamily = null,
        ReadingTheme? requiredTheme = null)
    {
        ValidatePositiveLength(minimumFontSize, nameof(minimumFontSize));
        ValidatePositiveLength(maximumFontSize, nameof(maximumFontSize));
        ValidateNonNegativeLength(minimumParagraphSpacing, nameof(minimumParagraphSpacing));
        ValidateNonNegativeLength(minimumContentMargin, nameof(minimumContentMargin));
        ValidateFontRange(minimumFontSize, maximumFontSize);
        ValidateFontFamily(requiredFontFamily);

        if (!double.IsFinite(minimumLineHeight) || minimumLineHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLineHeight),
                minimumLineHeight,
                "Minimum line height must be finite and greater than zero.");
        }

        if (!double.IsFinite(maximumLineHeight) || maximumLineHeight < minimumLineHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLineHeight),
                maximumLineHeight,
                "Maximum line height must be finite and not less than the minimum.");
        }

        if (requiredTheme is not null && !Enum.IsDefined(requiredTheme.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(requiredTheme), requiredTheme, "The reading theme is not defined.");
        }

        MinimumFontSize = minimumFontSize;
        MaximumFontSize = maximumFontSize;
        MinimumLineHeight = minimumLineHeight;
        MaximumLineHeight = maximumLineHeight;
        MinimumParagraphSpacing = minimumParagraphSpacing;
        MinimumContentMargin = minimumContentMargin;
        RequiredFontFamily = requiredFontFamily;
        RequiredTheme = requiredTheme;
    }

    public static RendererSafetyConstraints Default { get; } = new(
        minimumFontSize: Length.Px(12),
        maximumFontSize: Length.Px(96));

    public Length? MinimumFontSize { get; }

    public Length? MaximumFontSize { get; }

    public double MinimumLineHeight { get; }

    public double MaximumLineHeight { get; }

    public Length? MinimumParagraphSpacing { get; }

    public Length? MinimumContentMargin { get; }

    public string? RequiredFontFamily { get; }

    public ReadingTheme? RequiredTheme { get; }

    private static void ValidatePositiveLength(Length? value, string parameterName)
    {
        if (value is not null && value.Value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The length must be greater than zero.");
        }
    }

    private static void ValidateNonNegativeLength(Length? value, string parameterName)
    {
        if (value is not null && value.Value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The length cannot be negative.");
        }
    }

    private static void ValidateFontRange(Length? minimum, Length? maximum)
    {
        if (minimum is not null
            && maximum is not null
            && (minimum.Value.Unit != maximum.Value.Unit || minimum.Value.Value > maximum.Value.Value))
        {
            throw new ArgumentException("Font-size limits must use the same unit and form a valid range.");
        }
    }

    private static void ValidateFontFamily(string? value)
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
            throw new ArgumentException("A required font must be one semantic family name.", nameof(value));
        }
    }
}
