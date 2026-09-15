using System.Text.Json;
using Flow.Documents;

namespace Flow.Security;

internal static class CanonicalDocumentWriter
{
    internal static void Write(Utf8JsonWriter writer, FlowDocument document)
    {
        writer.WriteStartObject();
        writer.WriteString("canonicalization", FlowDocumentCanonicalizer.Version);

        writer.WritePropertyName("identity");
        writer.WriteStartObject();
        writer.WriteString("id", document.Identity.Id.Value);
        WriteNullableString(writer, "version", document.Identity.Version);
        WriteNullableString(writer, "previousVersionId", document.Identity.PreviousVersionId?.Value);
        writer.WriteEndObject();

        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        writer.WriteString("title", document.Metadata.Title);
        WriteNullableString(writer, "language", document.Metadata.Language);
        writer.WritePropertyName("authors");
        writer.WriteStartArray();
        foreach (var author in document.Metadata.Authors)
        {
            writer.WriteStringValue(author);
        }

        writer.WriteEndArray();
        WriteNullableString(writer, "subtitle", document.Metadata.Subtitle);
        WriteNullableString(writer, "description", document.Metadata.Description);
        writer.WriteEndObject();

        writer.WritePropertyName("content");
        WriteNodes(writer, document.Content.Children);

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
                WriteChildren(writer, chapter.Children);
                break;
            case Section section:
                WriteChildren(writer, section.Children);
                break;
            case Heading heading:
                writer.WriteNumber("level", heading.Level);
                WriteInlineProperty(writer, "content", heading.Content);
                break;
            case Paragraph paragraph:
                WriteInlineProperty(writer, "content", paragraph.Content);
                break;
            case BlockQuote blockQuote:
                WriteChildren(writer, blockQuote.Children);
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
                WriteChildren(writer, listItem.Children);
                break;
            case Figure figure:
                writer.WriteString("assetId", figure.AssetId.Value);
                WriteNullableString(writer, "alternativeText", figure.AlternativeText);
                writer.WritePropertyName("caption");
                if (figure.Caption is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    WriteNode(writer, figure.Caption);
                }

                break;
            case Caption caption:
                WriteInlineProperty(writer, "content", caption.Content);
                break;
            case Footnote footnote:
                WriteChildren(writer, footnote.Children);
                break;
            case Table table:
                WriteNullableNode(writer, "caption", table.Caption);
                WriteNullableNode(writer, "head", table.Head);
                writer.WritePropertyName("bodies");
                WriteNodes(writer, table.Bodies);
                WriteNullableNode(writer, "foot", table.Foot);
                break;
            case TableCaption tableCaption:
                WriteChildren(writer, tableCaption.Children);
                break;
            case TableHead tableHead:
                WriteNodeArray(writer, "rows", tableHead.Rows);
                break;
            case TableBody tableBody:
                WriteNodeArray(writer, "rows", tableBody.Rows);
                break;
            case TableFoot tableFoot:
                WriteNodeArray(writer, "rows", tableFoot.Rows);
                break;
            case TableRow tableRow:
                WriteNodeArray(writer, "cells", tableRow.Cells);
                break;
            case TableHeaderCell headerCell:
                WriteCell(writer, headerCell);
                WriteNullableString(writer, "scope", headerCell.Scope?.ToString());
                break;
            case TableCell tableCell:
                WriteCell(writer, tableCell);
                break;
            case MathExpression mathExpression:
                WriteMathElement(writer, "root", mathExpression.Root);
                WriteNullableString(writer, "alternativeText", mathExpression.AlternativeText);
                break;
            case HorizontalRule:
                break;
            case CodeBlock codeBlock:
                writer.WriteString("code", codeBlock.Code);
                WriteNullableString(writer, "language", codeBlock.Language);
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
                throw new NotSupportedException(
                    $"Node type '{node.GetType().FullName}' is not part of {FlowDocumentCanonicalizer.Version}.");
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
        Table => "table",
        TableCaption => "tableCaption",
        TableHead => "tableHead",
        TableBody => "tableBody",
        TableFoot => "tableFoot",
        TableRow => "tableRow",
        TableHeaderCell => "tableHeaderCell",
        TableCell => "tableCell",
        MathExpression => "mathExpression",
        HorizontalRule => "horizontalRule",
        CodeBlock => "codeBlock",
        TableOfContents => "tableOfContents",
        _ => throw new NotSupportedException(
            $"Node type '{node.GetType().FullName}' is not part of {FlowDocumentCanonicalizer.Version}."),
    };

    private static void WriteChildren(Utf8JsonWriter writer, IEnumerable<DocumentNode> children)
    {
        writer.WritePropertyName("children");
        WriteNodes(writer, children);
    }

    private static void WriteNullableNode(Utf8JsonWriter writer, string propertyName, DocumentNode? node)
    {
        writer.WritePropertyName(propertyName);
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            WriteNode(writer, node);
        }
    }

    private static void WriteNodeArray(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<DocumentNode> nodes)
    {
        writer.WritePropertyName(propertyName);
        WriteNodes(writer, nodes);
    }

    private static void WriteCell(Utf8JsonWriter writer, TableCellNode cell)
    {
        writer.WriteNumber("columnSpan", cell.ColumnSpan);
        writer.WriteNumber("rowSpan", cell.RowSpan);
        writer.WritePropertyName("headers");
        writer.WriteStartArray();
        foreach (var header in cell.Headers)
        {
            writer.WriteStringValue(header.Value);
        }

        writer.WriteEndArray();
        WriteChildren(writer, cell.Children);
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
            case Strong strong:
                WriteInlineProperty(writer, "children", strong.Children);
                break;
            case Emphasis emphasis:
                WriteInlineProperty(writer, "children", emphasis.Children);
                break;
            case Underline underline:
                WriteInlineProperty(writer, "children", underline.Children);
                break;
            case Strikethrough strikethrough:
                WriteInlineProperty(writer, "children", strikethrough.Children);
                break;
            case LanguageSpan languageSpan:
                writer.WriteString("language", languageSpan.Language.Value);
                WriteInlineProperty(writer, "children", languageSpan.Children);
                break;
            case BidirectionalSpan bidirectionalSpan:
                writer.WriteString("direction", bidirectionalSpan.Direction.ToString());
                writer.WriteString("mode", bidirectionalSpan.Mode.ToString());
                WriteInlineProperty(writer, "children", bidirectionalSpan.Children);
                break;
            case Ruby ruby:
                WriteInlineProperty(writer, "children", ruby.Children);
                break;
            case RubyAnnotation annotation:
                WriteInlineProperty(writer, "children", annotation.Children);
                break;
            case RubyFallbackParenthesis fallback:
                WriteInlineProperty(writer, "children", fallback.Children);
                break;
            case InlineCode inlineCode:
                writer.WriteString("code", inlineCode.Code);
                break;
            case Link link:
                writer.WriteString("target", link.Target);
                WriteInlineProperty(writer, "children", link.Children);
                break;
            case FootnoteReference footnoteReference:
                writer.WriteString("targetId", footnoteReference.TargetId.Value);
                if (!footnoteReference.Label.IsEmpty)
                {
                    WriteInlineProperty(writer, "label", footnoteReference.Label);
                }
                break;
            case InlineMath inlineMath:
                WriteMathElement(writer, "root", inlineMath.Root);
                WriteNullableString(writer, "alternativeText", inlineMath.AlternativeText);
                break;
            case LineBreak:
                break;
            default:
                throw new NotSupportedException(
                    $"Inline type '{node.GetType().FullName}' is not part of {FlowDocumentCanonicalizer.Version}.");
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
        InlineMath => "inlineMath",
        LanguageSpan => "languageSpan",
        BidirectionalSpan => "bidirectionalSpan",
        Ruby => "ruby",
        RubyAnnotation => "rubyAnnotation",
        RubyFallbackParenthesis => "rubyFallbackParenthesis",
        LineBreak => "lineBreak",
        _ => throw new NotSupportedException(
            $"Inline type '{node.GetType().FullName}' is not part of {FlowDocumentCanonicalizer.Version}."),
    };

    private static void WriteMathElement(Utf8JsonWriter writer, string propertyName, MathElement element)
    {
        writer.WritePropertyName(propertyName);
        WriteMathNode(writer, element);
    }

    private static void WriteMathNode(Utf8JsonWriter writer, MathNode node)
    {
        writer.WriteStartObject();
        switch (node)
        {
            case MathText text:
                writer.WriteString("type", "text");
                writer.WriteString("value", text.Value);
                break;
            case MathElement element:
                writer.WriteString("type", "element");
                writer.WriteString("name", element.Name);
                writer.WritePropertyName("attributes");
                writer.WriteStartObject();
                foreach (var attribute in element.Attributes)
                {
                    writer.WriteString(attribute.Key, attribute.Value);
                }

                writer.WriteEndObject();
                writer.WritePropertyName("children");
                writer.WriteStartArray();
                foreach (var child in element.Children)
                {
                    WriteMathNode(writer, child);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new NotSupportedException(
                    $"Math type '{node.GetType().FullName}' is not part of {FlowDocumentCanonicalizer.Version}.");
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
