using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class TableModelTests
{
    [Fact]
    public void Table_CopiesCollectionsAndRepresentsMultipleBodiesAndEmptyCells()
    {
        var headers = new List<NodeId> { new("header-name") };
        var emptyCell = new TableCell(new NodeId("cell-empty"), [], headers: headers);
        var firstRows = new List<TableRow>
        {
            new(
                new NodeId("row-one"),
                [new TableHeaderCell(new NodeId("header-name"), [Paragraph("p-header", "Name")], scope: TableHeaderScope.Column)]),
        };
        var bodies = new List<TableBody>
        {
            new(new NodeId("body-one"), firstRows),
            new(new NodeId("body-two"), [new TableRow(new NodeId("row-two"), [emptyCell])]),
        };

        var table = new Table(
            new NodeId("table-people"),
            bodies,
            new TableCaption(new NodeId("table-caption"), [Paragraph("p-caption", "People")]));
        headers.Clear();
        firstRows.Clear();
        bodies.Clear();

        Assert.Equal(2, table.Bodies.Length);
        Assert.Empty(emptyCell.Children);
        Assert.Equal(new NodeId("header-name"), Assert.Single(emptyCell.Headers));
        Assert.NotNull(table.Caption);
    }

    [Fact]
    public void TableCells_EnforcePositiveSpansAndDefinedScope()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableCell(new NodeId("cell-zero-column"), [], columnSpan: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableCell(new NodeId("cell-zero-row"), [], rowSpan: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableHeaderCell(
                new NodeId("header-invalid-scope"),
                [],
                scope: (TableHeaderScope)999));
    }

    [Fact]
    public void Validator_AcceptsAccessibleHeadersAndRejectsForeignHeaderTargets()
    {
        var headerId = new NodeId("header-name");
        var valid = DocumentWithTable(new Table(
            new NodeId("table-valid"),
            [
                new TableBody(
                    new NodeId("body-valid"),
                    [
                        new TableRow(
                            new NodeId("row-valid"),
                            [
                                new TableHeaderCell(headerId, [Paragraph("p-header", "Name")], scope: TableHeaderScope.Row),
                                new TableCell(new NodeId("cell-valid"), [Paragraph("p-value", "Ada")], headers: [headerId]),
                            ]),
                    ]),
            ]));
        var invalid = DocumentWithTable(new Table(
            new NodeId("table-invalid"),
            [
                new TableBody(
                    new NodeId("body-invalid"),
                    [
                        new TableRow(
                            new NodeId("row-invalid"),
                            [new TableCell(new NodeId("cell-invalid"), [], headers: [new NodeId("missing-header")])]),
                    ]),
            ]));

        Assert.True(new DocumentValidator().Validate(valid).IsValid);
        Assert.Contains(
            new DocumentValidator().Validate(invalid).Diagnostics,
            static diagnostic => diagnostic.Code == ValidationDiagnosticCodes.UnresolvedTableHeaderReference);
    }

    [Fact]
    public void Validator_RejectsTablePartsOutsideTheirRequiredParents()
    {
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:test:invalid-table-hierarchy")),
            new DocumentMetadata("Invalid table hierarchy"),
            new DocumentContent([new TableRow(new NodeId("orphan-row"), [])]));

        Assert.Contains(
            new DocumentValidator().Validate(document).Diagnostics,
            static diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidHierarchy);
    }

    private static FlowDocument DocumentWithTable(Table table) => new(
        new DocumentIdentity(new DocumentId($"urn:test:{table.Id.Value}")),
        new DocumentMetadata("Table"),
        new DocumentContent([new Chapter(new NodeId("chapter-one"), [table])]));

    private static Paragraph Paragraph(string id, string text) =>
        new(new NodeId(id), [new Text(text)]);
}
