using Flow.Documents;

namespace Flow.Layout;

/// <summary>Purely resolves Flow defaults, author presentation, reader preferences, and safety constraints.</summary>
public sealed class TypographyResolver
{
    public ResolvedReadingStyle Resolve(
        FlowDocument document,
        UserReadingPreferences? userPreferences = null,
        RendererSafetyConstraints? rendererConstraints = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Resolve(document.Presentation, userPreferences, rendererConstraints);
    }

    /// <summary>Resolves one optional node-specific author style through reader and safety precedence.</summary>
    public ResolvedTypographyStyle? ResolveNode(
        DocumentPresentation? presentation,
        Flow.Core.NodeId nodeId,
        TypographyRole role,
        UserReadingPreferences? userPreferences = null,
        RendererSafetyConstraints? rendererConstraints = null)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        if (presentation is null || !presentation.NodeTypography.TryGetValue(nodeId, out var nodeStyle))
        {
            return null;
        }

        var style = FlowStyleDefaults.Create().Typography[role];
        if (presentation.Typography.TryGetStyle(role, out var roleStyle))
        {
            style = Merge(style, roleStyle!);
        }

        style = Merge(style, nodeStyle);
        style = ApplyUserPreference(role, style, userPreferences);
        return ApplyRendererConstraint(role, style, rendererConstraints ?? RendererSafetyConstraints.Default);
    }

    public ResolvedReadingStyle Resolve(
        DocumentPresentation? documentPresentation,
        UserReadingPreferences? userPreferences = null,
        RendererSafetyConstraints? rendererConstraints = null)
    {
        var defaults = FlowStyleDefaults.Create();
        var constraints = rendererConstraints ?? RendererSafetyConstraints.Default;
        var styles = defaults.Typography.Styles.ToDictionary();

        ApplyDocumentPresentation(styles, documentPresentation);
        ApplyUserPreferences(styles, userPreferences);
        ApplyRendererConstraints(styles, constraints);

        var contentMargin = userPreferences?.ContentMargin ?? defaults.ContentMargin;
        contentMargin = EnforceMinimum(contentMargin, constraints.MinimumContentMargin);

        var theme = documentPresentation?.Theme ?? defaults.Theme;
        theme = userPreferences?.Theme ?? theme;
        theme = constraints.RequiredTheme ?? theme;

        return new ResolvedReadingStyle(
            new ResolvedTypographySet(styles),
            contentMargin,
            theme);
    }

    private static void ApplyDocumentPresentation(
        Dictionary<TypographyRole, ResolvedTypographyStyle> styles,
        DocumentPresentation? presentation)
    {
        if (presentation is null)
        {
            return;
        }

        foreach (var pair in presentation.Typography.Styles)
        {
            styles[pair.Key] = Merge(styles[pair.Key], pair.Value);
        }
    }

    private static void ApplyUserPreferences(
        Dictionary<TypographyRole, ResolvedTypographyStyle> styles,
        UserReadingPreferences? preferences)
    {
        if (preferences is null)
        {
            return;
        }

        foreach (var role in styles.Keys.ToArray())
        {
            styles[role] = ApplyUserPreference(role, styles[role], preferences);
        }
    }

    private static void ApplyRendererConstraints(
        Dictionary<TypographyRole, ResolvedTypographyStyle> styles,
        RendererSafetyConstraints constraints)
    {
        foreach (var role in styles.Keys.ToArray())
        {
            styles[role] = ApplyRendererConstraint(role, styles[role], constraints);
        }
    }

    private static ResolvedTypographyStyle Merge(
        ResolvedTypographyStyle inherited,
        TypographyStyle declared) =>
        new(
            declared.FontFamily ?? inherited.FontFamily,
            declared.FontSize ?? inherited.FontSize,
            declared.FontWeight ?? inherited.FontWeight,
            declared.FontStyle ?? inherited.FontStyle,
            declared.LineHeight ?? inherited.LineHeight,
            declared.LetterSpacing ?? inherited.LetterSpacing,
            declared.TextAlignment ?? inherited.TextAlignment,
            declared.TextTransform ?? inherited.TextTransform,
            declared.MarginBefore ?? inherited.MarginBefore,
            declared.MarginAfter ?? inherited.MarginAfter,
            declared.Indent ?? inherited.Indent,
            declared.TextDecoration ?? inherited.TextDecoration);

    private static ResolvedTypographyStyle ApplyUserPreference(
        TypographyRole role,
        ResolvedTypographyStyle style,
        UserReadingPreferences? preferences)
    {
        if (preferences is null)
        {
            return style;
        }

        var scale = preferences.FontScale;
        string? fontFamily = null;
        if (FlowStyleDefaults.IsHeadingRole(role))
        {
            scale *= preferences.HeadingScale;
            fontFamily = preferences.PreferredHeadingFont;
        }
        else if (FlowStyleDefaults.IsBodyFontRole(role))
        {
            fontFamily = preferences.PreferredBodyFont;
        }

        return style.With(
            fontFamily: fontFamily,
            fontSize: style.FontSize.Scale(scale),
            lineHeight: style.LineHeight * preferences.LineHeightScale,
            marginAfter: role == TypographyRole.Body ? preferences.ParagraphSpacing : null);
    }

    private static ResolvedTypographyStyle ApplyRendererConstraint(
        TypographyRole role,
        ResolvedTypographyStyle style,
        RendererSafetyConstraints constraints)
    {
        var fontSize = EnforceMaximum(EnforceMinimum(style.FontSize, constraints.MinimumFontSize), constraints.MaximumFontSize);
        var marginAfter = role == TypographyRole.Body
            ? EnforceMinimum(style.MarginAfter, constraints.MinimumParagraphSpacing)
            : style.MarginAfter;
        return style.With(
            fontFamily: constraints.RequiredFontFamily,
            fontSize: fontSize,
            lineHeight: Math.Clamp(style.LineHeight, constraints.MinimumLineHeight, constraints.MaximumLineHeight),
            marginAfter: marginAfter);
    }

    private static Length EnforceMinimum(Length value, Length? minimum) =>
        minimum is not null
        && minimum.Value.Unit == value.Unit
        && value.Value < minimum.Value.Value
            ? minimum.Value
            : value;

    private static Length EnforceMaximum(Length value, Length? maximum) =>
        maximum is not null
        && maximum.Value.Unit == value.Unit
        && value.Value > maximum.Value.Value
            ? maximum.Value
            : value;
}
