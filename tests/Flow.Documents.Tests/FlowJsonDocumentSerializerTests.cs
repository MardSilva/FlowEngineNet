using System.Text;
using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class FlowJsonDocumentSerializerTests
{
    private readonly FlowJsonDocumentSerializer _serializer = new();

    [Fact]
    public async Task SerializeAsync_ProducesDeterministicReadableJsonAndSortedAssets()
    {
        var document = CreateCompleteDocument();

        var first = await SerializeAsync(document);
        var second = await SerializeAsync(document);
        var json = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
        Assert.Contains("\"format\": \"flow-json-0.1\"", json, StringComparison.Ordinal);
        Assert.True(
            json.IndexOf("\"id\": \"asset-a.bin\"", StringComparison.Ordinal)
            < json.IndexOf("\"id\": \"asset-z.bin\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SerializeAsync_WritesUnicodeLiterallyAndEscapesUnsafeJsonText()
    {
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:test:readable-unicode")),
            new DocumentMetadata("Isto é 日本語 العربية 😀"),
            new DocumentContent(
            [
                new Paragraph(
                    new NodeId("unicode"),
                    [new Text("aspas \" e linha\nnova <script>alert('x')</script>")]),
            ]));

        var bytes = await SerializeAsync(document);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains("Isto é", json, StringComparison.Ordinal);
        Assert.Contains("日本語", json, StringComparison.Ordinal);
        Assert.Contains("العربية", json, StringComparison.Ordinal);
        Assert.Contains("\\uD83D\\uDE00", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u00E9", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003Cscript\\u003E", json, StringComparison.Ordinal);
        Assert.Contains("\\n", json, StringComparison.Ordinal);

        await using var source = new MemoryStream(bytes);
        var restored = await _serializer.DeserializeAsync(source);
        Assert.Equal(document.Metadata.Title, restored.Metadata.Title);
    }

    [Fact]
    public async Task RoundTrip_PreservesCompleteDocumentRepresentation()
    {
        var originalBytes = await SerializeAsync(CreateCompleteDocument());

        await using var input = new MemoryStream(originalBytes);
        var restored = await _serializer.DeserializeAsync(input);
        var restoredBytes = await SerializeAsync(restored);

        Assert.Equal(originalBytes, restoredBytes);
        Assert.Equal("The Flow Experiment", restored.Metadata.Title);
        Assert.Equal(2, restored.Assets.Count);
        Assert.Equal(ReadingTheme.Dark, restored.Presentation?.Theme);
        Assert.Equal(new NodeId("figure-one"), restored.Presentation?.Cover?.FigureId);
        Assert.Equal("ABC123", restored.Integrity?.Hash);
        Assert.IsType<Chapter>(Assert.Single(restored.Content.Children));
    }

    [Fact]
    public async Task RoundTrip_PreservesCompleteTableRepresentation()
    {
        var headerId = new NodeId("header-product");
        var table = new Table(
            new NodeId("table-products"),
            [
                new TableBody(
                    new NodeId("body-primary"),
                    [
                        new TableRow(
                            new NodeId("row-product"),
                            [
                                new TableHeaderCell(
                                    headerId,
                                    [Paragraph("p-header-product", "Product")],
                                    columnSpan: 2,
                                    scope: TableHeaderScope.Column),
                                new TableCell(
                                    new NodeId("cell-flow"),
                                    [Paragraph("p-cell-flow", "Flow")],
                                    rowSpan: 2,
                                    headers: [headerId]),
                                new TableCell(new NodeId("cell-empty"), []),
                            ]),
                    ]),
                new TableBody(new NodeId("body-secondary"), []),
            ],
            new TableCaption(new NodeId("caption-products"), [Paragraph("p-caption-products", "Products")]),
            new TableHead(new NodeId("head-products"), []),
            new TableFoot(new NodeId("foot-products"), []));
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:test:table-json")),
            new DocumentMetadata("Table JSON"),
            new DocumentContent([new Chapter(new NodeId("chapter-table"), [table])]));

        var first = await SerializeAsync(document);
        await using var input = new MemoryStream(first);
        var restored = await _serializer.DeserializeAsync(input);
        var second = await SerializeAsync(restored);

        Assert.Equal(first, second);
        var restoredTable = Assert.IsType<Table>(restored.Index.GetLocations(table.Id).Single().Node);
        Assert.Equal(2, restoredTable.Bodies.Length);
        var header = Assert.IsType<TableHeaderCell>(restoredTable.Bodies[0].Rows[0].Cells[0]);
        Assert.Equal(2, header.ColumnSpan);
        Assert.Equal(TableHeaderScope.Column, header.Scope);
        Assert.Empty(restoredTable.Bodies[0].Rows[0].Cells[2].Children);
    }

    [Fact]
    public async Task DeserializeAsync_MalformedJsonProvidesLocationDiagnostic()
    {
        await using var input = new MemoryStream("{\n  \"format\": "u8.ToArray());

        var exception = await Assert.ThrowsAsync<FlowSerializationException>(
            () => _serializer.DeserializeAsync(input));

        Assert.Equal(FlowSerializationDiagnosticCodes.InvalidJson, exception.Code);
        Assert.NotNull(exception.LineNumber);
        Assert.Contains("Invalid .flow.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializeAsync_MissingRequiredPropertyProvidesPathDiagnostic()
    {
        const string json = """
            {
              "format": "flow-json-0.1",
              "identity": { "id": "urn:flow:document:invalid" },
              "metadata": { "authors": [] },
              "content": { "nodes": [] },
              "assets": []
            }
            """;
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<FlowSerializationException>(
            () => _serializer.DeserializeAsync(input));

        Assert.Equal(FlowSerializationDiagnosticCodes.InvalidDocument, exception.Code);
        Assert.Equal("$.metadata.title", exception.Path);
        Assert.Contains("Required property", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializeAsync_RejectsUnsupportedFormat()
    {
        const string json = """
            {
              "format": "flow-json-9.9",
              "identity": {},
              "metadata": {},
              "content": {},
              "assets": []
            }
            """;
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<FlowSerializationException>(
            () => _serializer.DeserializeAsync(input));

        Assert.Equal(FlowSerializationDiagnosticCodes.UnsupportedFormat, exception.Code);
        Assert.Equal("$.format", exception.Path);
    }

    private async Task<byte[]> SerializeAsync(FlowDocument document)
    {
        await using var output = new MemoryStream();
        await _serializer.SerializeAsync(document, output);
        return output.ToArray();
    }

    private static FlowDocument CreateCompleteDocument()
    {
        var footnoteId = new NodeId("footnote-one");
        var headingAnchor = DocumentAnchor.Parse("flow:chapter-one/heading-one");
        var chapter = new Chapter(
            new NodeId("chapter-one"),
            [
                new TableOfContents(
                    new NodeId("toc-main"),
                    [new Text("Contents")],
                    [new TableOfContentsEntry([new Text("Chapter")], headingAnchor, 1)]),
                new Heading(new NodeId("heading-one"), 1, [new Text("Chapter one")]),
                new Paragraph(
                    new NodeId("p-rich"),
                    [
                        new Text("Text "),
                        new Strong([new Text("strong")]),
                        new Emphasis([new Text("emphasis")]),
                        new Underline([new Text("underline")]),
                        new Strikethrough([new Text("strike")]),
                        new InlineCode("code"),
                        new Link(headingAnchor.Value, [new Text("link")]),
                        new FootnoteReference(footnoteId, [new Strong([new Text("1")])]),
                        new LineBreak(),
                    ]),
                new BlockQuote(
                    new NodeId("quote-one"),
                    [new Paragraph(new NodeId("p-quote"), [new Text("Quoted")])]),
                new OrderedList(
                    new NodeId("ordered-one"),
                    [new ListItem(new NodeId("item-one"), [Paragraph("p-item-one", "First")])],
                    3),
                new UnorderedList(
                    new NodeId("unordered-one"),
                    [new ListItem(new NodeId("item-two"), [Paragraph("p-item-two", "Second")])]),
                new Figure(
                    new NodeId("figure-one"),
                    new AssetId("asset-z.bin"),
                    new Caption(new NodeId("caption-one"), [new Text("Caption")]),
                    "Alternative"),
                new Footnote(footnoteId, [Paragraph("p-footnote", "Footnote")]),
                new HorizontalRule(new NodeId("rule-one")),
                new CodeBlock(new NodeId("code-one"), "Document != Layout", "text"),
                new Section(new NodeId("section-one"), [Paragraph("p-section", "Section")]),
            ]);
        var typography = new TypographySet(
        [
            KeyValuePair.Create(
                TypographyRole.Body,
                new TypographyStyle(
                    "Literata",
                    Length.Px(18),
                    FontWeight.Normal,
                    FontStyle.Normal,
                    1.5,
                    Length.Em(0.01),
                    TextAlignment.Start,
                    TextTransform.None,
                    Length.Rem(1),
                    Length.Rem(1),
                    Length.Rem(0.5))),
        ]);
        var presentation = new DocumentPresentation(
            typography,
            new HeadingPresentation(true, true),
            new ParagraphPresentation(true, false),
            new FigurePresentation(FigureImportance.Essential, true, PreferredPlacement.Block, Length.Percent(100)),
            new CaptionPresentation(true),
            new FootnotePresentation(FootnotePresentationMode.EndOfSection),
            new CodeBlockPresentation(true, true),
            new TableOfContentsPresentation(true, TableOfContentsLeaderStyle.Dots),
            ReadingTheme.Dark,
            cover: new CoverPresentation(new NodeId("figure-one")));

        return new FlowDocument(
            new DocumentIdentity(
                new DocumentId("urn:flow:document:serializer-tests"),
                "1",
                new DocumentId("urn:flow:document:serializer-tests:previous")),
            new DocumentMetadata(
                "The Flow Experiment",
                "en",
                ["Author One", "Author Two"],
                "Subtitle",
                "Description"),
            new DocumentContent([chapter]),
            [
                new FlowAsset(new AssetId("asset-z.bin"), "application/octet-stream", "z.bin", new byte[] { 3 }),
                new FlowAsset(new AssetId("asset-a.bin"), "application/octet-stream", "a.bin", new byte[] { 1, 2 }),
            ],
            presentation,
            new DocumentIntegrity("SHA-256", "ABC123", "flow-c14n-0.1"));
    }

    private static Paragraph Paragraph(string id, string text) =>
        new(new NodeId(id), [new Text(text)]);
}
