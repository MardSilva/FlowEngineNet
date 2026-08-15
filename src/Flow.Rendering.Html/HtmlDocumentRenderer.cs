using System.Globalization;
using System.Net;
using System.Text;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering;

namespace Flow.Rendering.Html;

/// <summary>Produces deterministic standalone semantic HTML5 with embedded CSS and assets.</summary>
public sealed class HtmlDocumentRenderer : IDocumentRenderer
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public RenderedDocument Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(userPreferences);

        ValidateInputs(document, layout);

        var html = new HtmlDocumentWriter(document, layout).Write();
        return new RenderedDocument("text/html; charset=utf-8", ".html", Utf8WithoutBom.GetBytes(html));
    }

    /// <summary>Renders a standalone HTML document as a UTF-16 .NET string.</summary>
    public string RenderToString(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences) =>
        Render(document, layout, userPreferences).ReadAsUtf8();

    /// <summary>Renders and writes a standalone HTML document to a file.</summary>
    public void RenderToFile(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        string outputPath) =>
        Render(document, layout, userPreferences).WriteTo(outputPath);

    private static void ValidateInputs(FlowDocument document, LayoutDocument layout)
    {
        var validation = new DocumentValidator().Validate(document);
        if (!validation.IsValid)
        {
            var codes = string.Join(", ", validation.Errors.Select(static diagnostic => diagnostic.Code).Distinct());
            throw new ArgumentException(
                $"The semantic document is invalid and cannot be rendered. Diagnostics: {codes}.",
                nameof(document));
        }

        if (document.Identity.Id != layout.DocumentId
            || !string.Equals(document.Identity.Version, layout.DocumentVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The layout identity does not match the semantic document.", nameof(layout));
        }

        if (layout.ReadingMode != ReadingMode.Flow)
        {
            throw new NotSupportedException("The HTML renderer supports ReadingMode.Flow only in 0.1.");
        }

        var layoutNodes = Flatten(layout.Nodes).ToArray();
        if (layoutNodes.Length != document.Index.NodeCount
            || layoutNodes.Select(static node => node.SemanticId).Distinct().Count() != layoutNodes.Length)
        {
            throw new ArgumentException("The layout tree does not contain each semantic node exactly once.", nameof(layout));
        }

        foreach (var layoutNode in layoutNodes)
        {
            if (!document.Index.TryGetUniqueNode(layoutNode.SemanticId, out var semanticNode)
                || !ReferenceEquals(semanticNode, layoutNode.SemanticNode))
            {
                throw new ArgumentException(
                    $"Layout node '{layoutNode.SemanticId}' does not match the semantic document.",
                    nameof(layout));
            }
        }
    }

    private static IEnumerable<LayoutNode> Flatten(IEnumerable<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private sealed class HtmlDocumentWriter
    {
        private readonly FlowDocument _document;
        private readonly LayoutDocument _layout;
        private readonly StringBuilder _output = new();

        public HtmlDocumentWriter(FlowDocument document, LayoutDocument layout)
        {
            _document = document;
            _layout = layout;
        }

        public string Write()
        {
            Line("<!DOCTYPE html>");
            Line($"<html{LanguageAttribute(_document.Metadata.Language)}>");
            Line("<head>");
            Line("<meta charset=\"utf-8\" />");
            Line("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            Line("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\" />");
            Line($"<title>{Text(_document.Metadata.Title)}</title>");
            WriteStyles();
            Line("</head>");
            Line("<body>");
            Line($"<article data-document-id=\"{Attribute(_document.Identity.Id.Value)}\">");

            foreach (var node in _layout.Nodes)
            {
                WriteNode(node, parent: null);
            }

            Line("</article>");
            Line("</body>");
            Line("</html>");
            return _output.ToString();
        }

        private void WriteStyles()
        {
            var colors = ThemeColors(_layout.ReadingStyle.Theme);
            var profile = _layout.Profile;

            Line("<style>");
            Line(":root {");
            Line($"  color-scheme: {colors.ColorScheme};");
            Line($"  background: {colors.Background};");
            Line($"  color: {colors.Foreground};");
            Line("}");
            Line("* { box-sizing: border-box; }");
            Line("html, body { margin: 0; min-height: 100%; padding: 0; }");
            Line("body { background: inherit; color: inherit; }");
            Line("article {");
            WriteTypographyDeclarations(_layout.ReadingStyle.Typography[TypographyRole.Body]);
            Line($"  column-count: {profile.ColumnCount.ToString(CultureInfo.InvariantCulture)};");
            Line($"  column-gap: {CssLength(profile.ColumnGap)};");
            Line("  margin: 0 auto;");
            Line($"  max-width: {CssLength(profile.MaximumContentWidth)};");
            Line($"  padding: {CssLength(profile.ContentMargin)};");
            Line("}");
            WriteRoleStyle("p, li", TypographyRole.Body);
            WriteRoleStyle("[data-typography=\"chapter-title\"]", TypographyRole.ChapterTitle);
            WriteRoleStyle("[data-typography=\"heading-1\"]", TypographyRole.Heading1);
            WriteRoleStyle("[data-typography=\"heading-2\"]", TypographyRole.Heading2);
            WriteRoleStyle("[data-typography=\"heading-3\"]", TypographyRole.Heading3);
            WriteRoleStyle("[data-typography=\"heading-4\"]", TypographyRole.Heading4);
            WriteRoleStyle("[data-typography=\"heading-5\"]", TypographyRole.Heading5);
            WriteRoleStyle("[data-typography=\"heading-6\"]", TypographyRole.Heading6);
            WriteRoleStyle("[data-typography=\"subtitle\"]", TypographyRole.Subtitle);
            WriteRoleStyle("nav > h2", TypographyRole.TableOfContentsTitle);
            WriteRoleStyle("nav li[data-level=\"1\"]", TypographyRole.TableOfContentsLevel1);
            WriteRoleStyle("nav li[data-level=\"2\"]", TypographyRole.TableOfContentsLevel2);
            WriteRoleStyle("nav li[data-level=\"3\"]", TypographyRole.TableOfContentsLevel3);
            WriteRoleStyle("figcaption", TypographyRole.Caption);
            WriteRoleStyle("[role=\"doc-footnote\"], [role=\"doc-footnote\"] p", TypographyRole.Footnote);
            WriteRoleStyle("blockquote, blockquote p", TypographyRole.BlockQuote);
            WriteRoleStyle("pre, code", TypographyRole.Code);
            Line("nav, blockquote { break-inside: avoid; }");
            Line("figure { margin-inline: auto; }");
            Line("figure img { display: block; height: auto; max-width: 100%; width: 100%; }");
            Line("figcaption { margin-top: 0.5rem; }");
            Line("pre { max-width: 100%; overflow-x: auto; }");
            Line("nav ol { list-style: none; padding-inline-start: 0; }");
            Line("nav li[data-level=\"2\"] { padding-inline-start: 1.5rem; }");
            Line("nav li[data-level=\"3\"] { padding-inline-start: 3rem; }");
            Line("a { color: inherit; text-decoration-thickness: from-font; }");
            Line("@media (max-width: 599px) {");
            Line("  article { column-count: 1; max-width: 100%; padding: 16px; }");
            Line("  figure { float: none; max-width: 100% !important; }");
            Line("}");
            Line("</style>");
        }

        private void WriteRoleStyle(string selector, TypographyRole role)
        {
            Line($"{selector} {{");
            WriteTypographyDeclarations(_layout.ReadingStyle.Typography[role]);
            Line("}");
        }

        private void WriteTypographyDeclarations(ResolvedTypographyStyle style)
        {
            Line($"  font-family: \"{CssString(style.FontFamily)}\";");
            Line($"  font-size: {CssLength(style.FontSize)};");
            Line($"  font-style: {style.FontStyle.ToString().ToLowerInvariant()};");
            Line($"  font-weight: {((int)style.FontWeight).ToString(CultureInfo.InvariantCulture)};");
            Line($"  letter-spacing: {CssLength(style.LetterSpacing)};");
            Line($"  line-height: {CssNumber(style.LineHeight)};");
            Line($"  margin-block-start: {CssLength(style.MarginBefore)};");
            Line($"  margin-block-end: {CssLength(style.MarginAfter)};");
            Line($"  text-align: {CssTextAlignment(style.TextAlignment)};");
            Line($"  text-indent: {CssLength(style.Indent)};");
            Line($"  text-transform: {style.TextTransform.ToString().ToLowerInvariant()};");
        }

        private void WriteNode(LayoutNode layoutNode, DocumentNode? parent)
        {
            switch (layoutNode.SemanticNode)
            {
                case Chapter:
                case Section:
                    WriteContainer("section", layoutNode);
                    break;
                case Heading heading:
                    WriteHeading(layoutNode, heading, parent);
                    break;
                case Paragraph paragraph:
                    Line($"<p id=\"{Id(paragraph.Id)}\">{Inline(paragraph.Content)}</p>");
                    break;
                case BlockQuote:
                    WriteContainer("blockquote", layoutNode, " data-typography=\"blockquote\"");
                    break;
                case OrderedList orderedList:
                    WriteList("ol", layoutNode, orderedList.Start);
                    break;
                case UnorderedList:
                    WriteList("ul", layoutNode);
                    break;
                case ListItem:
                    WriteContainer("li", layoutNode);
                    break;
                case Figure figure:
                    WriteFigure(layoutNode, figure);
                    break;
                case Caption caption:
                    Line($"<figcaption id=\"{Id(caption.Id)}\">{Inline(caption.Content)}</figcaption>");
                    break;
                case Footnote:
                    WriteContainer("section", layoutNode, " role=\"doc-footnote\"");
                    break;
                case HorizontalRule rule:
                    Line($"<hr id=\"{Id(rule.Id)}\" />");
                    break;
                case CodeBlock codeBlock:
                    WriteCodeBlock(layoutNode, codeBlock);
                    break;
                case TableOfContents tableOfContents:
                    WriteTableOfContents(tableOfContents);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Semantic node kind '{layoutNode.SemanticNode.GetType().Name}' is not supported by this renderer.");
            }
        }

        private void WriteContainer(string element, LayoutNode node, string attributes = "")
        {
            Line($"<{element} id=\"{Id(node.SemanticId)}\"{attributes}>");
            foreach (var child in node.Children)
            {
                WriteNode(child, node.SemanticNode);
            }

            Line($"</{element}>");
        }

        private void WriteHeading(LayoutNode layoutNode, Heading heading, DocumentNode? parent)
        {
            var role = parent is Chapter && heading.Level == 1
                ? "chapter-title"
                : $"heading-{heading.Level.ToString(CultureInfo.InvariantCulture)}";
            var breakStyle = layoutNode.Intent is HeadingLayoutIntent { KeepWithNext: true }
                or HeadingLayoutIntent { AvoidBreakAfter: true }
                ? " style=\"break-after: avoid;\""
                : string.Empty;

            Line(
                $"<h{heading.Level} id=\"{Id(heading.Id)}\" data-typography=\"{role}\"{breakStyle}>"
                + $"{Inline(heading.Content)}</h{heading.Level}>");
        }

        private void WriteList(string element, LayoutNode node, int start = 1)
        {
            var startAttribute = element == "ol" && start != 1
                ? $" start=\"{start.ToString(CultureInfo.InvariantCulture)}\""
                : string.Empty;
            Line($"<{element} id=\"{Id(node.SemanticId)}\"{startAttribute}>");
            foreach (var item in node.Children)
            {
                WriteNode(item, node.SemanticNode);
            }

            Line($"</{element}>");
        }

        private void WriteFigure(LayoutNode layoutNode, Figure figure)
        {
            var asset = _document.Assets[figure.AssetId];
            var intent = layoutNode.Intent as FigureLayoutIntent
                ?? throw new ArgumentException($"Figure '{figure.Id}' has no resolved figure intent.", nameof(layoutNode));
            var keepStyle = intent.KeepWithCaption ? " break-inside: avoid;" : string.Empty;
            var placementStyle = intent.PreferredPlacement switch
            {
                PreferredPlacement.RendererChoice => string.Empty,
                PreferredPlacement.Inline => " display: inline-block;",
                PreferredPlacement.Block => " display: block; float: none;",
                PreferredPlacement.FloatStart => " float: inline-start;",
                PreferredPlacement.FloatEnd => " float: inline-end;",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(intent),
                    intent.PreferredPlacement,
                    "Unknown figure placement."),
            };
            var style = $"max-width: {CssLength(intent.MaximumWidth)};{keepStyle}{placementStyle}";
            var source = $"data:{SafeMediaType(asset.MediaType)};base64,{Convert.ToBase64String(asset.Data.AsSpan())}";

            Line($"<figure id=\"{Id(figure.Id)}\" style=\"{Attribute(style)}\">");
            Line(
                $"<img src=\"{Attribute(source)}\" alt=\"{Attribute(figure.AlternativeText ?? string.Empty)}\" />");
            foreach (var child in layoutNode.Children)
            {
                WriteNode(child, figure);
            }

            Line("</figure>");
        }

        private void WriteCodeBlock(LayoutNode layoutNode, CodeBlock codeBlock)
        {
            var intent = layoutNode.Intent as CodeBlockLayoutIntent
                ?? throw new ArgumentException($"Code block '{codeBlock.Id}' has no resolved code intent.", nameof(layoutNode));
            var styles = new List<string>(2);
            if (intent.AvoidSplit)
            {
                styles.Add("break-inside: avoid;");
            }

            styles.Add(intent.PreserveWhitespace ? "white-space: pre-wrap;" : "white-space: pre-line;");
            var style = string.Join(' ', styles);
            var language = codeBlock.Language is null
                ? string.Empty
                : $" data-language=\"{Attribute(codeBlock.Language)}\"";

            Line($"<pre id=\"{Id(codeBlock.Id)}\" style=\"{Attribute(style)}\"><code{language}>{Text(codeBlock.Code)}</code></pre>");
        }

        private void WriteTableOfContents(TableOfContents tableOfContents)
        {
            Line($"<nav id=\"{Id(tableOfContents.Id)}\" aria-label=\"Table of contents\">");
            if (!tableOfContents.Title.IsEmpty)
            {
                Line($"<h2>{Inline(tableOfContents.Title)}</h2>");
            }

            Line("<ol>");
            foreach (var entry in tableOfContents.Entries)
            {
                Line(
                    $"<li data-level=\"{entry.Level.ToString(CultureInfo.InvariantCulture)}\">"
                    + $"<a href=\"#{Id(entry.Target.TargetId)}\">{Inline(entry.Label)}</a></li>");
            }

            Line("</ol>");
            Line("</nav>");
        }

        private static string Inline(IEnumerable<InlineNode> nodes)
        {
            var output = new StringBuilder();
            foreach (var node in nodes)
            {
                WriteInline(output, node);
            }

            return output.ToString();
        }

        private static void WriteInline(StringBuilder output, InlineNode node)
        {
            switch (node)
            {
                case Text text:
                    output.Append(Text(text.Value));
                    break;
                case Strong strong:
                    WriteInlineContainer(output, "strong", strong.Children);
                    break;
                case Emphasis emphasis:
                    WriteInlineContainer(output, "em", emphasis.Children);
                    break;
                case Underline underline:
                    WriteInlineContainer(output, "u", underline.Children);
                    break;
                case Strikethrough strikethrough:
                    WriteInlineContainer(output, "s", strikethrough.Children);
                    break;
                case InlineCode code:
                    output.Append("<code>").Append(Text(code.Code)).Append("</code>");
                    break;
                case Link link:
                    WriteLink(output, link);
                    break;
                case FootnoteReference reference:
                    output.Append("<a href=\"#")
                        .Append(Id(reference.TargetId))
                        .Append("\" role=\"doc-noteref\" aria-label=\"Footnote\"><sup>note</sup></a>");
                    break;
                case LineBreak:
                    output.Append("<br />");
                    break;
                default:
                    throw new NotSupportedException($"Inline node kind '{node.GetType().Name}' is not supported by this renderer.");
            }
        }

        private static void WriteInlineContainer(
            StringBuilder output,
            string element,
            IEnumerable<InlineNode> children)
        {
            output.Append('<').Append(element).Append('>');
            foreach (var child in children)
            {
                WriteInline(output, child);
            }

            output.Append("</").Append(element).Append('>');
        }

        private static void WriteLink(StringBuilder output, Link link)
        {
            var href = SafeHref(link.Target);
            if (href is null)
            {
                WriteInlineContainer(output, "span", link.Children);
                return;
            }

            output.Append("<a href=\"").Append(Attribute(href)).Append("\">");
            foreach (var child in link.Children)
            {
                WriteInline(output, child);
            }

            output.Append("</a>");
        }

        private static string? SafeHref(string target)
        {
            if (DocumentAnchor.TryParse(target, out var anchor))
            {
                return $"#{anchor.TargetId.Value}";
            }

            if (target.StartsWith('#') && NodeId.TryParse(target[1..], out var nodeId))
            {
                return $"#{nodeId.Value}";
            }

            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri.Scheme is "http" or "https" or "mailto" ? target : null;
        }

        private static string SafeMediaType(string mediaType)
        {
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '/' and not '+' and not '-' and not '.'))
            {
                return "application/octet-stream";
            }

            return mediaType.ToLowerInvariant();
        }

        private static string LanguageAttribute(string? language) =>
            language is null ? string.Empty : $" lang=\"{Attribute(language)}\"";

        private static string Id(NodeId id) => Attribute(id.Value);

        private static string Text(string value) => WebUtility.HtmlEncode(value);

        private static string Attribute(string value) => WebUtility.HtmlEncode(value);

        private static string CssLength(Length length) =>
            CssNumber(length.Value) + (length.Unit switch
            {
                LengthUnit.Pixel => "px",
                LengthUnit.RootEm => "rem",
                LengthUnit.Em => "em",
                LengthUnit.Percent => "%",
                _ => throw new ArgumentOutOfRangeException(nameof(length), length, "Unknown length unit."),
            });

        private static string CssNumber(double value) =>
            value.ToString("0.################", CultureInfo.InvariantCulture);

        private static string CssTextAlignment(TextAlignment alignment) => alignment switch
        {
            TextAlignment.Start => "start",
            TextAlignment.Center => "center",
            TextAlignment.End => "end",
            TextAlignment.Justify => "justify",
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown text alignment."),
        };

        private static string CssString(string value)
        {
            var escaped = new StringBuilder(value.Length);
            foreach (var rune in value.EnumerateRunes())
            {
                if (rune.Value is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or ' '
                    or '-')
                {
                    escaped.Append(rune.ToString());
                }
                else
                {
                    escaped.Append('\\')
                        .Append(rune.Value.ToString("X", CultureInfo.InvariantCulture))
                        .Append(' ');
                }
            }

            return escaped.ToString();
        }

        private static (string ColorScheme, string Background, string Foreground) ThemeColors(ReadingTheme theme) =>
            theme switch
            {
                ReadingTheme.System => ("light dark", "Canvas", "CanvasText"),
                ReadingTheme.Light => ("light", "#ffffff", "#1a1a1a"),
                ReadingTheme.Dark => ("dark", "#171717", "#f2f2f2"),
                ReadingTheme.Sepia => ("light", "#f4ecd8", "#3b2f24"),
                ReadingTheme.HighContrast => ("light dark", "Canvas", "CanvasText"),
                _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown reading theme."),
            };

        private void Line(string value) => _output.Append(value).Append('\n');
    }
}
