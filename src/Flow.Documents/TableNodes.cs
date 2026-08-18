using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

/// <summary>Defines the semantic association of a table header cell.</summary>
public enum TableHeaderScope
{
    Row,
    Column,
    RowGroup,
    ColumnGroup,
}

/// <summary>Represents semantic tabular content without rendered geometry.</summary>
public sealed record Table : DocumentNode
{
    public Table(
        NodeId id,
        IEnumerable<TableBody> bodies,
        TableCaption? caption = null,
        TableHead? head = null,
        TableFoot? foot = null)
        : base(id)
    {
        Bodies = ImmutableCollections.CopyOf(bodies, nameof(bodies));
        Caption = caption;
        Head = head;
        Foot = foot;
    }

    public TableCaption? Caption { get; }

    public TableHead? Head { get; }

    public ImmutableArray<TableBody> Bodies { get; }

    public TableFoot? Foot { get; }
}

/// <summary>Contains the semantic caption content of a table.</summary>
public sealed record TableCaption : BlockContainerNode
{
    public TableCaption(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}

public sealed record TableHead : DocumentNode
{
    public TableHead(NodeId id, IEnumerable<TableRow> rows)
        : base(id)
    {
        Rows = ImmutableCollections.CopyOf(rows, nameof(rows));
    }

    public ImmutableArray<TableRow> Rows { get; }
}

public sealed record TableBody : DocumentNode
{
    public TableBody(NodeId id, IEnumerable<TableRow> rows)
        : base(id)
    {
        Rows = ImmutableCollections.CopyOf(rows, nameof(rows));
    }

    public ImmutableArray<TableRow> Rows { get; }
}

public sealed record TableFoot : DocumentNode
{
    public TableFoot(NodeId id, IEnumerable<TableRow> rows)
        : base(id)
    {
        Rows = ImmutableCollections.CopyOf(rows, nameof(rows));
    }

    public ImmutableArray<TableRow> Rows { get; }
}

public sealed record TableRow : DocumentNode
{
    public TableRow(NodeId id, IEnumerable<TableCellNode> cells)
        : base(id)
    {
        Cells = ImmutableCollections.CopyOf(cells, nameof(cells));
    }

    public ImmutableArray<TableCellNode> Cells { get; }
}

public abstract record TableCellNode : BlockContainerNode
{
    protected TableCellNode(
        NodeId id,
        IEnumerable<DocumentNode> children,
        int columnSpan,
        int rowSpan,
        IEnumerable<NodeId>? headers)
        : base(id, children)
    {
        if (columnSpan < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnSpan),
                columnSpan,
                "A table cell column span must be positive.");
        }

        if (rowSpan < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowSpan),
                rowSpan,
                "A table cell row span must be positive.");
        }

        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
        Headers = ImmutableCollections.CopyOf(headers ?? [], nameof(headers));
    }

    public int ColumnSpan { get; }

    public int RowSpan { get; }

    public ImmutableArray<NodeId> Headers { get; }
}

public sealed record TableHeaderCell : TableCellNode
{
    public TableHeaderCell(
        NodeId id,
        IEnumerable<DocumentNode> children,
        int columnSpan = 1,
        int rowSpan = 1,
        TableHeaderScope? scope = null,
        IEnumerable<NodeId>? headers = null)
        : base(id, children, columnSpan, rowSpan, headers)
    {
        if (scope is not null && !Enum.IsDefined(scope.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "The table header scope is not defined.");
        }

        Scope = scope;
    }

    public TableHeaderScope? Scope { get; }
}

public sealed record TableCell : TableCellNode
{
    public TableCell(
        NodeId id,
        IEnumerable<DocumentNode> children,
        int columnSpan = 1,
        int rowSpan = 1,
        IEnumerable<NodeId>? headers = null)
        : base(id, children, columnSpan, rowSpan, headers)
    {
    }
}
