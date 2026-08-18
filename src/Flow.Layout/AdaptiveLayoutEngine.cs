using Flow.Documents;

namespace Flow.Layout;

/// <summary>Implements the renderer-independent adaptive layout profile for <see cref="ReadingMode.Flow" />.</summary>
public sealed class AdaptiveLayoutEngine : ILayoutEngine
{
    public const double MediumViewportMinimumWidth = 600;
    public const double LargeViewportMinimumWidth = 1200;

    private readonly DocumentValidator _validator;
    private readonly TypographyResolver _typographyResolver;

    public AdaptiveLayoutEngine()
        : this(new DocumentValidator(), new TypographyResolver())
    {
    }

    public AdaptiveLayoutEngine(
        DocumentValidator validator,
        TypographyResolver typographyResolver)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(typographyResolver);

        _validator = validator;
        _typographyResolver = typographyResolver;
    }

    /// <inheritdoc />
    public LayoutDocument Layout(FlowDocument document, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ReadingMode != ReadingMode.Flow)
        {
            throw new NotSupportedException(
                $"Reading mode '{context.ReadingMode}' is not implemented. Flow is the only mode supported in 0.1.");
        }

        var validation = _validator.Validate(document);
        if (!validation.IsValid)
        {
            var codes = string.Join(", ", validation.Errors.Select(static diagnostic => diagnostic.Code).Distinct());
            throw new ArgumentException(
                $"The semantic document is invalid and cannot be laid out. Diagnostics: {codes}.",
                nameof(document));
        }

        var profile = CreateProfile(context);
        var style = _typographyResolver.Resolve(
            document,
            context.UserPreferences,
            context.RendererConstraints);
        var contentMargin = ResolveContentMargin(profile.ContentMargin, style.ContentMargin, context);
        profile = new LayoutProfile(
            profile.ViewportCategory,
            profile.ColumnCount,
            contentMargin,
            profile.MaximumContentWidth,
            profile.ColumnGap,
            profile.ResponsiveFigures);
        style = new ResolvedReadingStyle(style.Typography, contentMargin, style.Theme);
        var nodes = document.Content.Children.Select(node => CreateNode(
            node,
            parent: null,
            document.Presentation,
            profile,
            context.UserPreferences,
            context.RendererConstraints));

        return new LayoutDocument(
            document.Identity.Id,
            document.Identity.Version,
            context.ReadingMode,
            profile,
            style,
            nodes);
    }

    private static LayoutProfile CreateProfile(LayoutContext context)
    {
        if (context.ViewportWidth < MediumViewportMinimumWidth)
        {
            return new LayoutProfile(
                ViewportCategory.Small,
                columnCount: 1,
                contentMargin: Length.Px(16),
                maximumContentWidth: Length.Percent(100),
                columnGap: Length.Px(0),
                responsiveFigures: true);
        }

        if (context.ViewportWidth < LargeViewportMinimumWidth)
        {
            return new LayoutProfile(
                ViewportCategory.Medium,
                columnCount: 1,
                contentMargin: Length.Px(32),
                maximumContentWidth: Length.Px(800),
                columnGap: Length.Px(0),
                responsiveFigures: true);
        }

        var columnCount = context.AllowTwoColumns ? 2 : 1;
        return new LayoutProfile(
            ViewportCategory.Large,
            columnCount,
            contentMargin: Length.Px(64),
            maximumContentWidth: Length.Px(1120),
            columnGap: Length.Px(columnCount == 2 ? 48 : 0),
            responsiveFigures: true);
    }

    private static Length ResolveContentMargin(
        Length adaptiveDefault,
        Length cascadeResult,
        LayoutContext context)
    {
        if (context.UserPreferences?.ContentMargin is not null)
        {
            return cascadeResult;
        }

        var minimum = context.RendererConstraints?.MinimumContentMargin;
        return minimum is not null
            && minimum.Value.Unit == adaptiveDefault.Unit
            && minimum.Value.Value > adaptiveDefault.Value
                ? minimum.Value
                : adaptiveDefault;
    }

    private LayoutNode CreateNode(
        DocumentNode node,
        DocumentNode? parent,
        DocumentPresentation? presentation,
        LayoutProfile profile,
        UserReadingPreferences? userPreferences,
        RendererSafetyConstraints? rendererConstraints)
    {
        var children = GetChildren(node).Select(child => CreateNode(
            child,
            node,
            presentation,
            profile,
            userPreferences,
            rendererConstraints));
        var typography = _typographyResolver.ResolveNode(
            presentation,
            node.Id,
            ResolveTypographyRole(node, parent),
            userPreferences,
            rendererConstraints);
        return new LayoutNode(node.Id, node, ResolveIntent(node, presentation, profile), children, typography);
    }

    private static TypographyRole ResolveTypographyRole(DocumentNode node, DocumentNode? parent) => node switch
    {
        Heading { Level: 1 } when parent is Chapter => TypographyRole.ChapterTitle,
        Heading heading => heading.Level switch
        {
            1 => TypographyRole.Heading1,
            2 => TypographyRole.Heading2,
            3 => TypographyRole.Heading3,
            4 => TypographyRole.Heading4,
            5 => TypographyRole.Heading5,
            6 => TypographyRole.Heading6,
            _ => TypographyRole.Body,
        },
        Caption => TypographyRole.Caption,
        TableCaption => TypographyRole.Caption,
        Footnote => TypographyRole.Footnote,
        CodeBlock => TypographyRole.Code,
        BlockQuote => TypographyRole.BlockQuote,
        Paragraph when parent is BlockQuote => TypographyRole.BlockQuote,
        Paragraph when parent is Footnote => TypographyRole.Footnote,
        _ => TypographyRole.Body,
    };

    private static IEnumerable<DocumentNode> GetChildren(DocumentNode node) => node switch
    {
        BlockContainerNode container => container.Children,
        OrderedList orderedList => orderedList.Items,
        UnorderedList unorderedList => unorderedList.Items,
        Figure { Caption: not null } figure => [figure.Caption],
        Table table => TableChildren(table),
        TableHead head => head.Rows,
        TableBody body => body.Rows,
        TableFoot foot => foot.Rows,
        TableRow row => row.Cells,
        _ => [],
    };

    private static IEnumerable<DocumentNode> TableChildren(Table table)
    {
        if (table.Caption is not null)
        {
            yield return table.Caption;
        }

        if (table.Head is not null)
        {
            yield return table.Head;
        }

        foreach (var body in table.Bodies)
        {
            yield return body;
        }

        if (table.Foot is not null)
        {
            yield return table.Foot;
        }
    }

    private static NodeLayoutIntent ResolveIntent(
        DocumentNode node,
        DocumentPresentation? presentation,
        LayoutProfile profile) => node switch
        {
            Heading => new HeadingLayoutIntent(
                presentation?.Headings?.KeepWithNext ?? true,
                presentation?.Headings?.AvoidBreakAfter ?? true),
            Figure => CreateFigureIntent(presentation?.Figures, profile),
            CodeBlock => new CodeBlockLayoutIntent(
                presentation?.CodeBlocks?.AvoidSplit ?? true,
                presentation?.CodeBlocks?.PreserveWhitespace ?? true),
            Footnote => new FootnoteLayoutIntent(
                presentation?.Footnotes?.PreferredPresentation
                    ?? FootnotePresentationMode.RendererChoice),
            _ => DefaultLayoutIntent.Instance,
        };

    private static FigureLayoutIntent CreateFigureIntent(
        FigurePresentation? presentation,
        LayoutProfile profile)
    {
        var preferredPlacement = profile.ViewportCategory == ViewportCategory.Small
            ? PreferredPlacement.Block
            : presentation?.PreferredPlacement ?? PreferredPlacement.RendererChoice;

        return new FigureLayoutIntent(
            presentation?.KeepWithCaption ?? true,
            presentation?.MaximumWidth ?? Length.Percent(100),
            ScaleDownToFit: profile.ResponsiveFigures,
            preferredPlacement);
    }
}
