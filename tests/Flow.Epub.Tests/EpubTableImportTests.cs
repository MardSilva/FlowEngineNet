using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubTableImportTests
{
    [Fact]
    public async Task ImportAsync_PreservesAccessibleTableThroughJsonLayoutAndHtml()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body><h1>Tables</h1>
                <table id="schedule">
                  <caption id="schedule-caption">Schedule</caption>
                  <thead id="schedule-head"><tr id="header-row">
                    <th id="header-day" scope="col">Day</th>
                    <th id="header-topic" scope="col">Topic</th>
                  </tr></thead>
                  <tbody id="primary-body">
                    <tr id="row-one"><td headers="header-day" rowspan="2">Monday</td><td headers="header-topic">Model</td></tr>
                    <tr id="row-two"><td headers="header-topic"></td></tr>
                  </tbody>
                  <tbody id="secondary-body">
                    <tr id="row-three"><td colspan="2">Complete</td></tr>
                  </tbody>
                  <tfoot id="schedule-foot"><tr><td colspan="2">Footer</td></tr></tfoot>
                </table>
              </body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, static diagnostic =>
            diagnostic.Code is EpubDiagnosticCodes.InvalidTableStructure
                or EpubDiagnosticCodes.InvalidTableSpan
                or EpubDiagnosticCodes.InvalidTableScope
                or EpubDiagnosticCodes.MissingTableHeader);
        var document = Assert.IsType<FlowDocument>(result.Document);
        var table = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Table>());
        Assert.NotNull(table.Caption);
        Assert.NotNull(table.Head);
        Assert.NotNull(table.Foot);
        Assert.Equal(2, table.Bodies.Length);
        var dayHeader = Assert.IsType<TableHeaderCell>(table.Head.Rows[0].Cells[0]);
        Assert.Equal(TableHeaderScope.Column, dayHeader.Scope);
        var monday = Assert.IsType<TableCell>(table.Bodies[0].Rows[0].Cells[0]);
        Assert.Equal(2, monday.RowSpan);
        Assert.Equal(dayHeader.Id, Assert.Single(monday.Headers));
        Assert.Empty(table.Bodies[0].Rows[1].Cells[0].Children);
        Assert.Equal(2, table.Bodies[1].Rows[0].Cells[0].ColumnSpan);

        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var parsed = XDocument.Parse(html);

        var renderedTable = Assert.Single(parsed.Descendants("table"));
        Assert.Single(renderedTable.Elements("caption"));
        Assert.Equal(2, renderedTable.Elements("tbody").Count());
        var renderedHeader = renderedTable.Descendants("th").First();
        Assert.Equal("col", (string?)renderedHeader.Attribute("scope"));
        var renderedMonday = renderedTable.Descendants("td").First();
        Assert.Equal("2", (string?)renderedMonday.Attribute("rowspan"));
        Assert.Equal(dayHeader.Id.Value, (string?)renderedMonday.Attribute("headers"));
        Assert.Contains(renderedTable.Descendants("td"), static cell => !cell.Nodes().Any());
    }

    [Fact]
    public async Task ImportAsync_AllowsIrregularRowsWithoutInventingAVisualGrid()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <table><tbody>
                <tr><td colspan="3">Wide</td></tr>
                <tr><td>Narrow one</td><td>Narrow two</td></tr>
              </tbody></table>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var table = Assert.Single(result.Document!.Index.Locations.Select(static item => item.Node).OfType<Table>());
        Assert.Equal(3, table.Bodies[0].Rows[0].Cells[0].ColumnSpan);
        Assert.Equal(2, table.Bodies[0].Rows[1].Cells.Length);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.InvalidTableStructure);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesMalformedTableAndPreservesEveryRecoverableCellContent()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <table>
                <caption>Primary caption</caption><caption>Additional caption</caption>
                <td colspan="0" rowspan="many" scope="row">Direct cell</td>
                <tbody><tr>
                  <td>A</td><div>B</div><td headers="missing-header">C</td><td></td>
                </tr></tbody>
              </table>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.InvalidTableStructure);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.InvalidTableSpan);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.InvalidTableScope);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.MissingTableHeader);
        var table = Assert.Single(result.Document!.Index.Locations.Select(static item => item.Node).OfType<Table>());
        var text = NodeText(table);
        foreach (var expected in new[] { "Primary caption", "Additional caption", "Direct cell", "A", "B", "C" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        Assert.Contains(
            table.Bodies.SelectMany(static body => body.Rows).SelectMany(static row => row.Cells),
            static cell => cell.Children.IsEmpty);
    }

    private static string NodeText(DocumentNode node) => node switch
    {
        Paragraph paragraph => InlineText(paragraph.Content),
        Heading heading => InlineText(heading.Content),
        Caption caption => InlineText(caption.Content),
        _ => string.Concat(Children(node).Select(NodeText)),
    };

    private static IEnumerable<DocumentNode> Children(DocumentNode node) => node switch
    {
        BlockContainerNode container => container.Children,
        Table table => TableChildren(table),
        TableHead head => head.Rows,
        TableBody body => body.Rows,
        TableFoot foot => foot.Rows,
        TableRow row => row.Cells,
        _ => [],
    };

    private static IEnumerable<DocumentNode> TableChildren(Table table) =>
        new DocumentNode?[] { table.Caption, table.Head }
            .Where(static node => node is not null)
            .Cast<DocumentNode>()
            .Concat(table.Bodies)
            .Concat(new DocumentNode?[] { table.Foot }.Where(static node => node is not null).Cast<DocumentNode>());

    private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
    {
        Text text => text.Value,
        InlineContainerNode container => InlineText(container.Children),
        InlineCode code => code.Code,
        FootnoteReference reference => InlineText(reference.Label),
        LineBreak => " ",
        _ => string.Empty,
    }));
}
