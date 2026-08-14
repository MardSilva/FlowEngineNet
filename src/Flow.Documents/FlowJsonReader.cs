using System.Collections.Immutable;
using System.Text.Json;
using Flow.Core;

namespace Flow.Documents;

internal static class FlowJsonReader
{
    internal static FlowDocument Read(JsonElement root)
    {
        RequireKind(root, JsonValueKind.Object, "$");
        var format = RequiredString(root, "format", "$.format");
        if (!string.Equals(format, FlowJsonWriter.FormatVersion, StringComparison.Ordinal))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.UnsupportedFormat,
                $"Unsupported Flow JSON format '{format}'. Expected '{FlowJsonWriter.FormatVersion}'.",
                "$.format");
        }

        var identityElement = RequiredProperty(root, "identity", "$.identity");
        var identity = new DocumentIdentity(
            new DocumentId(RequiredString(identityElement, "id", "$.identity.id")),
            OptionalString(identityElement, "version", "$.identity.version"),
            ReadOptionalDocumentId(identityElement, "previousVersionId", "$.identity.previousVersionId"));

        var metadataElement = RequiredProperty(root, "metadata", "$.metadata");
        var metadata = new DocumentMetadata(
            RequiredString(metadataElement, "title", "$.metadata.title"),
            OptionalString(metadataElement, "language", "$.metadata.language"),
            ReadStrings(RequiredProperty(metadataElement, "authors", "$.metadata.authors"), "$.metadata.authors"),
            OptionalString(metadataElement, "subtitle", "$.metadata.subtitle"),
            OptionalString(metadataElement, "description", "$.metadata.description"));

        var contentElement = RequiredProperty(root, "content", "$.content");
        var nodesElement = RequiredProperty(contentElement, "nodes", "$.content.nodes");
        var content = new DocumentContent(ReadNodes(nodesElement, "$.content.nodes"));

        var assets = ReadAssets(RequiredProperty(root, "assets", "$.assets"), "$.assets");
        var presentation = root.TryGetProperty("presentation", out var presentationElement)
            ? ReadPresentation(presentationElement, "$.presentation")
            : null;
        var integrity = root.TryGetProperty("integrity", out var integrityElement)
            ? ReadIntegrity(integrityElement, "$.integrity")
            : null;

        return new FlowDocument(identity, metadata, content, assets, presentation, integrity);
    }

    private static DocumentId? ReadOptionalDocumentId(JsonElement parent, string propertyName, string path)
    {
        var value = OptionalString(parent, propertyName, path);
        return value is null ? null : new DocumentId(value);
    }

    private static IEnumerable<string> ReadStrings(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Error(
                    FlowSerializationDiagnosticCodes.InvalidDocument,
                    $"Expected a string at '{path}[{index}]'.",
                    $"{path}[{index}]");
            }

            yield return item.GetString()!;
            index++;
        }
    }

    private static IEnumerable<FlowAsset> ReadAssets(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        var index = 0;
        foreach (var asset in element.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            RequireKind(asset, JsonValueKind.Object, itemPath);

            byte[] bytes;
            try
            {
                bytes = RequiredProperty(asset, "data", $"{itemPath}.data").GetBytesFromBase64();
            }
            catch (FormatException exception)
            {
                throw Error(
                    FlowSerializationDiagnosticCodes.InvalidDocument,
                    $"Asset data at '{itemPath}.data' is not valid Base64.",
                    $"{itemPath}.data",
                    exception);
            }

            yield return new FlowAsset(
                new AssetId(RequiredString(asset, "id", $"{itemPath}.id")),
                RequiredString(asset, "mediaType", $"{itemPath}.mediaType"),
                RequiredString(asset, "fileName", $"{itemPath}.fileName"),
                bytes);
            index++;
        }
    }

    private static IEnumerable<DocumentNode> ReadNodes(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        var index = 0;
        foreach (var node in element.EnumerateArray())
        {
            yield return ReadNode(node, $"{path}[{index}]");
            index++;
        }
    }

    private static DocumentNode ReadNode(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var type = RequiredString(element, "type", $"{path}.type");
        var id = new NodeId(RequiredString(element, "id", $"{path}.id"));

        return type switch
        {
            "chapter" => new Chapter(id, ReadChildren(element, path)),
            "section" => new Section(id, ReadChildren(element, path)),
            "heading" => new Heading(
                id,
                RequiredInt32(element, "level", $"{path}.level"),
                ReadInlineProperty(element, "content", path)),
            "paragraph" => new Paragraph(id, ReadInlineProperty(element, "content", path)),
            "blockQuote" => new BlockQuote(id, ReadChildren(element, path)),
            "orderedList" => new OrderedList(
                id,
                ReadListItems(element, path),
                RequiredInt32(element, "start", $"{path}.start")),
            "unorderedList" => new UnorderedList(id, ReadListItems(element, path)),
            "listItem" => new ListItem(id, ReadChildren(element, path)),
            "figure" => new Figure(
                id,
                new AssetId(RequiredString(element, "assetId", $"{path}.assetId")),
                ReadOptionalCaption(element, path),
                OptionalString(element, "alternativeText", $"{path}.alternativeText")),
            "caption" => new Caption(id, ReadInlineProperty(element, "content", path)),
            "footnote" => new Footnote(id, ReadChildren(element, path)),
            "horizontalRule" => new HorizontalRule(id),
            "codeBlock" => new CodeBlock(
                id,
                RequiredString(element, "code", $"{path}.code"),
                OptionalString(element, "language", $"{path}.language")),
            "tableOfContents" => new TableOfContents(
                id,
                ReadInlineProperty(element, "title", path),
                ReadTableOfContentsEntries(element, path),
                RequiredInt32(element, "maximumDepth", $"{path}.maximumDepth")),
            _ => throw Error(
                FlowSerializationDiagnosticCodes.UnsupportedNode,
                $"Unsupported document node type '{type}' at '{path}'.",
                $"{path}.type"),
        };
    }

    private static IEnumerable<DocumentNode> ReadChildren(JsonElement parent, string path) =>
        ReadNodes(RequiredProperty(parent, "children", $"{path}.children"), $"{path}.children");

    private static IEnumerable<ListItem> ReadListItems(JsonElement parent, string path)
    {
        var items = RequiredProperty(parent, "items", $"{path}.items");
        var index = 0;
        foreach (var node in ReadNodes(items, $"{path}.items"))
        {
            if (node is not ListItem listItem)
            {
                throw Error(
                    FlowSerializationDiagnosticCodes.InvalidDocument,
                    $"List entry at '{path}.items[{index}]' must be a listItem.",
                    $"{path}.items[{index}]");
            }

            yield return listItem;
            index++;
        }
    }

    private static Caption? ReadOptionalCaption(JsonElement parent, string path)
    {
        if (!parent.TryGetProperty("caption", out var captionElement))
        {
            return null;
        }

        var node = ReadNode(captionElement, $"{path}.caption");
        return node as Caption
            ?? throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Figure caption at '{path}.caption' must have type 'caption'.",
                $"{path}.caption.type");
    }

    private static IEnumerable<TableOfContentsEntry> ReadTableOfContentsEntries(
        JsonElement parent,
        string path)
    {
        var entries = RequiredProperty(parent, "entries", $"{path}.entries");
        RequireKind(entries, JsonValueKind.Array, $"{path}.entries");
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var itemPath = $"{path}.entries[{index}]";
            yield return new TableOfContentsEntry(
                ReadInlineProperty(entry, "label", itemPath),
                DocumentAnchor.Parse(RequiredString(entry, "target", $"{itemPath}.target")),
                RequiredInt32(entry, "level", $"{itemPath}.level"));
            index++;
        }
    }

    private static IEnumerable<InlineNode> ReadInlineProperty(
        JsonElement parent,
        string propertyName,
        string path)
    {
        var propertyPath = $"{path}.{propertyName}";
        var items = RequiredProperty(parent, propertyName, propertyPath);
        RequireKind(items, JsonValueKind.Array, propertyPath);
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            yield return ReadInline(item, $"{propertyPath}[{index}]");
            index++;
        }
    }

    private static InlineNode ReadInline(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var type = RequiredString(element, "type", $"{path}.type");

        return type switch
        {
            "text" => new Text(RequiredString(element, "value", $"{path}.value")),
            "strong" => new Strong(ReadInlineProperty(element, "children", path)),
            "emphasis" => new Emphasis(ReadInlineProperty(element, "children", path)),
            "underline" => new Underline(ReadInlineProperty(element, "children", path)),
            "strikethrough" => new Strikethrough(ReadInlineProperty(element, "children", path)),
            "inlineCode" => new InlineCode(RequiredString(element, "code", $"{path}.code")),
            "link" => new Link(
                RequiredString(element, "target", $"{path}.target"),
                ReadInlineProperty(element, "children", path)),
            "footnoteReference" => new FootnoteReference(
                new NodeId(RequiredString(element, "targetId", $"{path}.targetId"))),
            "lineBreak" => new LineBreak(),
            _ => throw Error(
                FlowSerializationDiagnosticCodes.UnsupportedNode,
                $"Unsupported inline node type '{type}' at '{path}'.",
                $"{path}.type"),
        };
    }

    private static DocumentPresentation ReadPresentation(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var typography = element.TryGetProperty("typography", out var typographyElement)
            ? ReadTypography(typographyElement, $"{path}.typography")
            : new TypographySet();

        return new DocumentPresentation(
            typography,
            ReadHeadingPresentation(element, path),
            ReadParagraphPresentation(element, path),
            ReadFigurePresentation(element, path),
            ReadCaptionPresentation(element, path),
            ReadFootnotePresentation(element, path),
            ReadCodeBlockPresentation(element, path),
            ReadTableOfContentsPresentation(element, path),
            OptionalEnum<ReadingTheme>(element, "theme", $"{path}.theme"));
    }

    private static TypographySet ReadTypography(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        var styles = ImmutableArray.CreateBuilder<KeyValuePair<TypographyRole, TypographyStyle>>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            var role = RequiredEnum<TypographyRole>(item, "role", $"{itemPath}.role");
            var styleElement = RequiredProperty(item, "style", $"{itemPath}.style");
            styles.Add(KeyValuePair.Create(role, ReadTypographyStyle(styleElement, $"{itemPath}.style")));
            index++;
        }

        return new TypographySet(styles);
    }

    private static TypographyStyle ReadTypographyStyle(JsonElement element, string path) =>
        new(
            OptionalString(element, "fontFamily", $"{path}.fontFamily"),
            OptionalLength(element, "fontSize", $"{path}.fontSize"),
            OptionalEnum<FontWeight>(element, "fontWeight", $"{path}.fontWeight"),
            OptionalEnum<FontStyle>(element, "fontStyle", $"{path}.fontStyle"),
            OptionalDouble(element, "lineHeight", $"{path}.lineHeight"),
            OptionalLength(element, "letterSpacing", $"{path}.letterSpacing"),
            OptionalEnum<TextAlignment>(element, "textAlignment", $"{path}.textAlignment"),
            OptionalEnum<TextTransform>(element, "textTransform", $"{path}.textTransform"),
            OptionalLength(element, "marginBefore", $"{path}.marginBefore"),
            OptionalLength(element, "marginAfter", $"{path}.marginAfter"),
            OptionalLength(element, "indent", $"{path}.indent"));

    private static HeadingPresentation? ReadHeadingPresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("headings", out var element)
            ? new HeadingPresentation(
                OptionalBoolean(element, "keepWithNext", $"{path}.headings.keepWithNext"),
                OptionalBoolean(element, "avoidBreakAfter", $"{path}.headings.avoidBreakAfter"))
            : null;

    private static ParagraphPresentation? ReadParagraphPresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("paragraphs", out var element)
            ? new ParagraphPresentation(
                OptionalBoolean(element, "keepTogether", $"{path}.paragraphs.keepTogether"),
                OptionalBoolean(element, "keepWithNext", $"{path}.paragraphs.keepWithNext"))
            : null;

    private static FigurePresentation? ReadFigurePresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("figures", out var element)
            ? new FigurePresentation(
                OptionalEnum<FigureImportance>(element, "importance", $"{path}.figures.importance"),
                OptionalBoolean(element, "keepWithCaption", $"{path}.figures.keepWithCaption"),
                OptionalEnum<PreferredPlacement>(element, "preferredPlacement", $"{path}.figures.preferredPlacement"),
                OptionalLength(element, "maximumWidth", $"{path}.figures.maximumWidth"))
            : null;

    private static CaptionPresentation? ReadCaptionPresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("captions", out var element)
            ? new CaptionPresentation(OptionalBoolean(element, "keepWithFigure", $"{path}.captions.keepWithFigure"))
            : null;

    private static FootnotePresentation? ReadFootnotePresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("footnotes", out var element)
            ? new FootnotePresentation(
                OptionalEnum<FootnotePresentationMode>(
                    element,
                    "preferredPresentation",
                    $"{path}.footnotes.preferredPresentation"))
            : null;

    private static CodeBlockPresentation? ReadCodeBlockPresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("codeBlocks", out var element)
            ? new CodeBlockPresentation(
                OptionalBoolean(element, "avoidSplit", $"{path}.codeBlocks.avoidSplit"),
                OptionalBoolean(element, "preserveWhitespace", $"{path}.codeBlocks.preserveWhitespace"))
            : null;

    private static TableOfContentsPresentation? ReadTableOfContentsPresentation(JsonElement parent, string path) =>
        parent.TryGetProperty("tableOfContents", out var element)
            ? new TableOfContentsPresentation(
                OptionalBoolean(
                    element,
                    "generateFromDocumentStructure",
                    $"{path}.tableOfContents.generateFromDocumentStructure"),
                OptionalEnum<TableOfContentsLeaderStyle>(
                    element,
                    "leaderStyle",
                    $"{path}.tableOfContents.leaderStyle"))
            : null;

    private static DocumentIntegrity ReadIntegrity(JsonElement element, string path) =>
        new(
            RequiredString(element, "algorithm", $"{path}.algorithm"),
            RequiredString(element, "hash", $"{path}.hash"),
            RequiredString(element, "canonicalizationVersion", $"{path}.canonicalizationVersion"));

    private static Length? OptionalLength(JsonElement parent, string propertyName, string path)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        RequireKind(element, JsonValueKind.Object, path);
        var value = RequiredDouble(element, "value", $"{path}.value");
        var unit = RequiredEnum<LengthUnit>(element, "unit", $"{path}.unit");
        return unit switch
        {
            LengthUnit.Pixel => Length.Px(value),
            LengthUnit.RootEm => Length.Rem(value),
            LengthUnit.Em => Length.Em(value),
            LengthUnit.Percent => Length.Percent(value),
            _ => throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Unsupported length unit '{unit}' at '{path}.unit'.",
                $"{path}.unit"),
        };
    }

    private static JsonElement RequiredProperty(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var property))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Required property '{path}' is missing.",
                path);
        }

        return property;
    }

    private static string RequiredString(JsonElement parent, string name, string path)
    {
        var property = RequiredProperty(parent, name, path);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a string.",
                path);
        }

        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a string or null.",
                path);
        }

        return property.GetString();
    }

    private static int RequiredInt32(JsonElement parent, string name, string path)
    {
        var property = RequiredProperty(parent, name, path);
        if (!property.TryGetInt32(out var value))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a 32-bit integer.",
                path);
        }

        return value;
    }

    private static double RequiredDouble(JsonElement parent, string name, string path)
    {
        var property = RequiredProperty(parent, name, path);
        if (!property.TryGetDouble(out var value))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a number.",
                path);
        }

        return value;
    }

    private static double? OptionalDouble(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!property.TryGetDouble(out var value))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a number or null.",
                path);
        }

        return value;
    }

    private static bool? OptionalBoolean(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a boolean or null.",
                path);
        }

        return property.GetBoolean();
    }

    private static TEnum RequiredEnum<TEnum>(JsonElement parent, string name, string path)
        where TEnum : struct, Enum
    {
        var text = RequiredString(parent, name, path);
        if (!Enum.TryParse<TEnum>(text, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' has unsupported value '{text}'.",
                path);
        }

        return value;
    }

    private static TEnum? OptionalEnum<TEnum>(JsonElement parent, string name, string path)
        where TEnum : struct, Enum
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' must be a string or null.",
                path);
        }

        var text = property.GetString()!;
        if (!Enum.TryParse<TEnum>(text, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Property '{path}' has unsupported value '{text}'.",
                path);
        }

        return value;
    }

    private static void RequireKind(JsonElement element, JsonValueKind expected, string path)
    {
        if (element.ValueKind != expected)
        {
            throw Error(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"Expected {expected} at '{path}', found {element.ValueKind}.",
                path);
        }
    }

    private static FlowSerializationException Error(
        string code,
        string message,
        string path,
        Exception? innerException = null) =>
        new(code, message, path, innerException: innerException);
}
