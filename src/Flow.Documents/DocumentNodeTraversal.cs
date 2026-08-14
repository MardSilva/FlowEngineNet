namespace Flow.Documents;

internal static class DocumentNodeTraversal
{
    internal static IEnumerable<DocumentNode> GetChildren(DocumentNode node) => node switch
    {
        BlockContainerNode container => container.Children,
        OrderedList orderedList => orderedList.Items,
        UnorderedList unorderedList => unorderedList.Items,
        Figure { Caption: not null } figure => [figure.Caption],
        _ => [],
    };
}
