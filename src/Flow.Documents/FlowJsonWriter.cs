using System.Text.Json;

namespace Flow.Documents;

internal static class FlowJsonWriter
{
    internal const string FormatVersion = "flow-json-0.1";

    internal static void Write(Utf8JsonWriter writer, FlowDocument document)
    {
        writer.WriteStartObject();
        writer.WriteString("format", FormatVersion);
        WriteIdentity(writer, document.Identity);
        WriteMetadata(writer, document.Metadata);

        writer.WritePropertyName("content");
        writer.WriteStartObject();
        writer.WritePropertyName("nodes");
        WriteNodes(writer, document.Content.Children);
        writer.WriteEndObject();

        writer.WritePropertyName("assets");
        writer.WriteStartArray();
        foreach (var asset in document.Assets.Values.OrderBy(static asset => asset.Id.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", asset.Id.Value);
            writer.WriteString("mediaType", asset.MediaType);
            writer.WriteString("fileName", asset.FileName);
            writer.WriteBase64String("data", asset.Data.AsSpan());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (document.Presentation is not null)
        {
            WritePresentation(writer, document.Presentation);
        }

        if (document.Integrity is not null)
        {
            writer.WritePropertyName("integrity");
            writer.WriteStartObject();
            writer.WriteString("algorithm", document.Integrity.Algorithm);
            writer.WriteString("hash", document.Integrity.Hash);
            writer.WriteString("canonicalizationVersion", document.Integrity.CanonicalizationVersion);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteIdentity(Utf8JsonWriter writer, DocumentIdentity identity)
    {
        writer.WritePropertyName("identity");
        writer.WriteStartObject();
        writer.WriteString("id", identity.Id.Value);
        WriteOptionalString(writer, "version", identity.Version);
        WriteOptionalString(writer, "previousVersionId", identity.PreviousVersionId?.Value);
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, DocumentMetadata metadata)
    {
        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        writer.WriteString("title", metadata.Title);
        WriteOptionalString(writer, "language", metadata.Language);
        writer.WritePropertyName("authors");
        writer.WriteStartArray();
        foreach (var author in metadata.Authors)
        {
            writer.WriteStringValue(author);
        }

        writer.WriteEndArray();
        WriteOptionalString(writer, "subtitle", metadata.Subtitle);
        WriteOptionalString(writer, "description", metadata.Description);
        writer.WriteEndObject();
    }

    private static void WriteNodes(Utf8JsonWriter writer, IEnumerable<DocumentNode> nodes)
    {
        writer.WriteStartArray();
        foreach (var node in nodes)
        {
            WriteNode(writer, node);
        }

        writer.WriteEndArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, DocumentNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("type", GetNodeType(node));
        writer.WriteString("id", node.Id.Value);

        switch (node)
        {
            case Chapter chapter:
                WriteChildNodes(writer, chapter.Children);
                break;
            case Section section:
                WriteChildNodes(writer, section.Children);
                break;
            case Heading heading:
                writer.WriteNumber("level", heading.Level);
                WriteInlineProperty(writer, "content", heading.Content);
                break;
            case Paragraph paragraph:
                WriteInlineProperty(writer, "content", paragraph.Content);
                break;
            case BlockQuote blockQuote:
                WriteChildNodes(writer, blockQuote.Children);
                break;
            case OrderedList orderedList:
                writer.WriteNumber("start", orderedList.Start);
                writer.WritePropertyName("items");
                WriteNodes(writer, orderedList.Items);
                break;
            case UnorderedList unorderedList:
                writer.WritePropertyName("items");
                WriteNodes(writer, unorderedList.Items);
                break;
            case ListItem listItem:
                WriteChildNodes(writer, listItem.Children);
                break;
            case Figure figure:
                writer.WriteString("assetId", figure.AssetId.Value);
                WriteOptionalString(writer, "alternativeText", figure.AlternativeText);
                if (figure.Caption is not null)
                {
                    writer.WritePropertyName("caption");
                    WriteNode(writer, figure.Caption);
                }

                break;
            case Caption caption:
                WriteInlineProperty(writer, "content", caption.Content);
                break;
            case Footnote footnote:
                WriteChildNodes(writer, footnote.Children);
                break;
            case HorizontalRule:
                break;
            case CodeBlock codeBlock:
                writer.WriteString("code", codeBlock.Code);
                WriteOptionalString(writer, "language", codeBlock.Language);
                break;
            case TableOfContents tableOfContents:
                WriteInlineProperty(writer, "title", tableOfContents.Title);
                writer.WriteNumber("maximumDepth", tableOfContents.MaximumDepth);
                writer.WritePropertyName("entries");
                writer.WriteStartArray();
                foreach (var entry in tableOfContents.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("level", entry.Level);
                    writer.WriteString("target", entry.Target.Value);
                    WriteInlineProperty(writer, "label", entry.Label);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            default:
                throw UnsupportedNode(node.GetType());
        }

        writer.WriteEndObject();
    }

    private static string GetNodeType(DocumentNode node) => node switch
    {
        Chapter => "chapter",
        Section => "section",
        Heading => "heading",
        Paragraph => "paragraph",
        BlockQuote => "blockQuote",
        OrderedList => "orderedList",
        UnorderedList => "unorderedList",
        ListItem => "listItem",
        Figure => "figure",
        Caption => "caption",
        Footnote => "footnote",
        HorizontalRule => "horizontalRule",
        CodeBlock => "codeBlock",
        TableOfContents => "tableOfContents",
        _ => throw UnsupportedNode(node.GetType()),
    };

    private static void WriteChildNodes(Utf8JsonWriter writer, IEnumerable<DocumentNode> children)
    {
        writer.WritePropertyName("children");
        WriteNodes(writer, children);
    }

    private static void WriteInlineProperty(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<InlineNode> nodes)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var node in nodes)
        {
            WriteInline(writer, node);
        }

        writer.WriteEndArray();
    }

    private static void WriteInline(Utf8JsonWriter writer, InlineNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("type", GetInlineType(node));

        switch (node)
        {
            case Text text:
                writer.WriteString("value", text.Value);
                break;
            case InlineContainerNode container:
                WriteInlineProperty(writer, "children", container.Children);
                if (container is Link link)
                {
                    writer.WriteString("target", link.Target);
                }

                break;
            case InlineCode inlineCode:
                writer.WriteString("code", inlineCode.Code);
                break;
            case FootnoteReference footnoteReference:
                writer.WriteString("targetId", footnoteReference.TargetId.Value);
                break;
            case LineBreak:
                break;
            default:
                throw UnsupportedNode(node.GetType());
        }

        writer.WriteEndObject();
    }

    private static string GetInlineType(InlineNode node) => node switch
    {
        Text => "text",
        Strong => "strong",
        Emphasis => "emphasis",
        Underline => "underline",
        Strikethrough => "strikethrough",
        InlineCode => "inlineCode",
        Link => "link",
        FootnoteReference => "footnoteReference",
        LineBreak => "lineBreak",
        _ => throw UnsupportedNode(node.GetType()),
    };

    private static void WritePresentation(Utf8JsonWriter writer, DocumentPresentation presentation)
    {
        writer.WritePropertyName("presentation");
        writer.WriteStartObject();
        if (presentation.Theme is not null)
        {
            writer.WriteString("theme", presentation.Theme.Value.ToString());
        }

        writer.WritePropertyName("typography");
        writer.WriteStartArray();
        foreach (var pair in presentation.Typography.Styles.OrderBy(static pair => pair.Key))
        {
            writer.WriteStartObject();
            writer.WriteString("role", pair.Key.ToString());
            writer.WritePropertyName("style");
            WriteTypographyStyle(writer, pair.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (!presentation.NodeTypography.IsEmpty)
        {
            writer.WritePropertyName("nodeTypography");
            writer.WriteStartArray();
            foreach (var pair in presentation.NodeTypography.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("nodeId", pair.Key.Value);
                writer.WritePropertyName("style");
                WriteTypographyStyle(writer, pair.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        WriteHeadingPresentation(writer, presentation.Headings);
        WriteParagraphPresentation(writer, presentation.Paragraphs);
        WriteFigurePresentation(writer, presentation.Figures);
        WriteCaptionPresentation(writer, presentation.Captions);
        WriteFootnotePresentation(writer, presentation.Footnotes);
        WriteCodeBlockPresentation(writer, presentation.CodeBlocks);
        WriteTableOfContentsPresentation(writer, presentation.TableOfContents);
        writer.WriteEndObject();
    }

    private static void WriteTypographyStyle(Utf8JsonWriter writer, TypographyStyle style)
    {
        writer.WriteStartObject();
        WriteOptionalString(writer, "fontFamily", style.FontFamily);
        WriteOptionalLength(writer, "fontSize", style.FontSize);
        WriteOptionalEnum(writer, "fontWeight", style.FontWeight);
        WriteOptionalEnum(writer, "fontStyle", style.FontStyle);
        WriteOptionalNumber(writer, "lineHeight", style.LineHeight);
        WriteOptionalLength(writer, "letterSpacing", style.LetterSpacing);
        WriteOptionalEnum(writer, "textAlignment", style.TextAlignment);
        WriteOptionalEnum(writer, "textTransform", style.TextTransform);
        WriteOptionalLength(writer, "marginBefore", style.MarginBefore);
        WriteOptionalLength(writer, "marginAfter", style.MarginAfter);
        WriteOptionalLength(writer, "indent", style.Indent);
        WriteOptionalEnum(writer, "textDecoration", style.TextDecoration);
        writer.WriteEndObject();
    }

    private static void WriteHeadingPresentation(Utf8JsonWriter writer, HeadingPresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("headings");
        writer.WriteStartObject();
        WriteOptionalBoolean(writer, "keepWithNext", value.KeepWithNext);
        WriteOptionalBoolean(writer, "avoidBreakAfter", value.AvoidBreakAfter);
        writer.WriteEndObject();
    }

    private static void WriteParagraphPresentation(Utf8JsonWriter writer, ParagraphPresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("paragraphs");
        writer.WriteStartObject();
        WriteOptionalBoolean(writer, "keepTogether", value.KeepTogether);
        WriteOptionalBoolean(writer, "keepWithNext", value.KeepWithNext);
        writer.WriteEndObject();
    }

    private static void WriteFigurePresentation(Utf8JsonWriter writer, FigurePresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("figures");
        writer.WriteStartObject();
        WriteOptionalEnum(writer, "importance", value.Importance);
        WriteOptionalBoolean(writer, "keepWithCaption", value.KeepWithCaption);
        WriteOptionalEnum(writer, "preferredPlacement", value.PreferredPlacement);
        WriteOptionalLength(writer, "maximumWidth", value.MaximumWidth);
        writer.WriteEndObject();
    }

    private static void WriteCaptionPresentation(Utf8JsonWriter writer, CaptionPresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("captions");
        writer.WriteStartObject();
        WriteOptionalBoolean(writer, "keepWithFigure", value.KeepWithFigure);
        writer.WriteEndObject();
    }

    private static void WriteFootnotePresentation(Utf8JsonWriter writer, FootnotePresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("footnotes");
        writer.WriteStartObject();
        WriteOptionalEnum(writer, "preferredPresentation", value.PreferredPresentation);
        writer.WriteEndObject();
    }

    private static void WriteCodeBlockPresentation(Utf8JsonWriter writer, CodeBlockPresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("codeBlocks");
        writer.WriteStartObject();
        WriteOptionalBoolean(writer, "avoidSplit", value.AvoidSplit);
        WriteOptionalBoolean(writer, "preserveWhitespace", value.PreserveWhitespace);
        writer.WriteEndObject();
    }

    private static void WriteTableOfContentsPresentation(
        Utf8JsonWriter writer,
        TableOfContentsPresentation? value)
    {
        if (value is null)
        {
            return;
        }

        writer.WritePropertyName("tableOfContents");
        writer.WriteStartObject();
        WriteOptionalBoolean(writer, "generateFromDocumentStructure", value.GenerateFromDocumentStructure);
        WriteOptionalEnum(writer, "leaderStyle", value.LeaderStyle);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteOptionalBoolean(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is not null)
        {
            writer.WriteBoolean(name, value.Value);
        }
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteOptionalEnum<TEnum>(Utf8JsonWriter writer, string name, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is not null)
        {
            writer.WriteString(name, value.Value.ToString());
        }
    }

    private static void WriteOptionalLength(Utf8JsonWriter writer, string name, Length? length)
    {
        if (length is null)
        {
            return;
        }

        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("value", length.Value.Value);
        writer.WriteString("unit", length.Value.Unit.ToString());
        writer.WriteEndObject();
    }

    private static FlowSerializationException UnsupportedNode(Type type) =>
        new(
            FlowSerializationDiagnosticCodes.UnsupportedNode,
            $"Type '{type.FullName}' is not supported by {FormatVersion}.");
}
