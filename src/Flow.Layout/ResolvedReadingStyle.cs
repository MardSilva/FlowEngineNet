using Flow.Documents;

namespace Flow.Layout;

public sealed record ResolvedReadingStyle
{
    internal ResolvedReadingStyle(
        ResolvedTypographySet typography,
        Length contentMargin,
        ReadingTheme theme)
    {
        Typography = typography;
        ContentMargin = contentMargin;
        Theme = theme;
    }

    public ResolvedTypographySet Typography { get; }

    public Length ContentMargin { get; }

    public ReadingTheme Theme { get; }
}
