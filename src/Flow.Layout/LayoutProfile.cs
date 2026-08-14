using Flow.Documents;

namespace Flow.Layout;

public enum ViewportCategory
{
    Small,
    Medium,
    Large,
}

public sealed record LayoutProfile
{
    public LayoutProfile(
        ViewportCategory viewportCategory,
        int columnCount,
        Length contentMargin,
        Length maximumContentWidth,
        Length columnGap,
        bool responsiveFigures)
    {
        if (!Enum.IsDefined(viewportCategory))
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewportCategory),
                viewportCategory,
                "The viewport category is not defined.");
        }

        if (columnCount is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount), columnCount, "Flow supports one or two columns.");
        }

        ValidateNonNegative(contentMargin, nameof(contentMargin));
        ValidatePositive(maximumContentWidth, nameof(maximumContentWidth));
        ValidateNonNegative(columnGap, nameof(columnGap));

        ViewportCategory = viewportCategory;
        ColumnCount = columnCount;
        ContentMargin = contentMargin;
        MaximumContentWidth = maximumContentWidth;
        ColumnGap = columnGap;
        ResponsiveFigures = responsiveFigures;
    }

    public ViewportCategory ViewportCategory { get; }

    public int ColumnCount { get; }

    public Length ContentMargin { get; }

    public Length MaximumContentWidth { get; }

    public Length ColumnGap { get; }

    public bool ResponsiveFigures { get; }

    private static void ValidateNonNegative(Length value, string parameterName)
    {
        if (value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The length cannot be negative.");
        }
    }

    private static void ValidatePositive(Length value, string parameterName)
    {
        if (value.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The length must be greater than zero.");
        }
    }
}
