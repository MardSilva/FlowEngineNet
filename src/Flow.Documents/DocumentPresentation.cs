using System.Collections.Immutable;
using Flow.Core;

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
        TableOfContentsPresentation? tableOfContents = null,
        ReadingTheme? theme = null,
        IEnumerable<KeyValuePair<NodeId, TypographyStyle>>? nodeTypography = null,
        CoverPresentation? cover = null)
    {
        PresentationIntentValidation.ValidateEnum(theme, nameof(theme));

        Typography = typography ?? new TypographySet();
        Headings = headings;
        Paragraphs = paragraphs;
        Figures = figures;
        Captions = captions;
        Footnotes = footnotes;
        CodeBlocks = codeBlocks;
        TableOfContents = tableOfContents;
        Theme = theme;
        Cover = cover;
        var nodeStyles = ImmutableDictionary.CreateBuilder<NodeId, TypographyStyle>();
        foreach (var pair in nodeTypography ?? [])
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!nodeStyles.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException($"Node typography ID '{pair.Key}' occurs more than once.", nameof(nodeTypography));
            }
        }

        NodeTypography = nodeStyles.ToImmutable();
    }

    public TypographySet Typography { get; }

    public HeadingPresentation? Headings { get; }

    public ParagraphPresentation? Paragraphs { get; }

    public FigurePresentation? Figures { get; }

    public CaptionPresentation? Captions { get; }

    public FootnotePresentation? Footnotes { get; }

    public CodeBlockPresentation? CodeBlocks { get; }

    public TableOfContentsPresentation? TableOfContents { get; }

    public ReadingTheme? Theme { get; }

    /// <summary>Gets the optional, noncanonical publication-cover intent.</summary>
    public CoverPresentation? Cover { get; }

    /// <summary>Gets optional typed author typography for individual semantic nodes.</summary>
    public ImmutableDictionary<NodeId, TypographyStyle> NodeTypography { get; }
}
