namespace Flow.Documents;

public enum FigureImportance
{
    Supporting,
    Normal,
    Essential,
}

public enum PreferredPlacement
{
    RendererChoice,
    Inline,
    Block,
    FloatStart,
    FloatEnd,
}

public enum FootnotePresentationMode
{
    RendererChoice,
    Inline,
    EndOfSection,
    Popover,
    Margin,
    BottomOfPage,
}

public enum TableOfContentsLeaderStyle
{
    None,
    Dots,
    Lines,
}

public enum ReadingTheme
{
    System,
    Light,
    Dark,
    Sepia,
    HighContrast,
}
