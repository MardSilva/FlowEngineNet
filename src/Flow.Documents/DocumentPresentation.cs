namespace Flow.Documents;

public sealed record DocumentPresentation
{
    public DocumentPresentation(
        TypographySet? typography = null,
        HeadingPresentation? headings = null,
        ParagraphPresentation? paragraphs = null,
        FigurePresentation? figures = null,
        CaptionPresentation? captions = null,
        FootnotePresentation? footnotes = null,
        CodeBlockPresentation? codeBlocks = null,
        TableOfContentsPresentation? tableOfContents = null)
    {
        Typography = typography ?? new TypographySet();
        Headings = headings;
        Paragraphs = paragraphs;
        Figures = figures;
        Captions = captions;
        Footnotes = footnotes;
        CodeBlocks = codeBlocks;
        TableOfContents = tableOfContents;
    }

    public TypographySet Typography { get; }

    public HeadingPresentation? Headings { get; }

    public ParagraphPresentation? Paragraphs { get; }

    public FigurePresentation? Figures { get; }

    public CaptionPresentation? Captions { get; }

    public FootnotePresentation? Footnotes { get; }

    public CodeBlockPresentation? CodeBlocks { get; }

    public TableOfContentsPresentation? TableOfContents { get; }
}
