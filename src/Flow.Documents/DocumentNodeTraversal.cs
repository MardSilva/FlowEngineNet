namespace Flow.Documents;

internal static class DocumentNodeTraversal
{
    internal static IEnumerable<DocumentNode> GetChildren(DocumentNode node) => node switch
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
}
