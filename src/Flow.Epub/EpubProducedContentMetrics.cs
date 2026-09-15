using Flow.Documents;

namespace Flow.Epub;

internal static class EpubProducedContentMetrics
{
    internal static long CountCharacters(FlowDocument document)
    {
        long total = 0;
        foreach (var location in document.Index.Locations)
        {
            total += location.Node switch
            {
                Heading value => CountInline(value.Content),
                Paragraph value => CountInline(value.Content),
                Caption value => CountInline(value.Content),
                CodeBlock value => value.Code.Length,
                Figure value => value.AlternativeText?.Length ?? 0,
                TableOfContents value => CountInline(value.Title)
                                             + value.Entries.Sum(static entry => CountInline(entry.Label)),
                MathExpression value => value.AlternativeText?.Length ?? 0,
                _ => 0,
            };
        }

        return total;
    }

    private static long CountInline(IEnumerable<InlineNode> nodes)
    {
        long total = 0;
        foreach (var node in nodes)
        {
            total += node switch
            {
                Text value => value.Value.Length,
                InlineCode value => value.Code.Length,
                FootnoteReference value => CountInline(value.Label),
                InlineContainerNode value => CountInline(value.Children),
                InlineMath value => value.AlternativeText?.Length ?? 0,
                _ => 0,
            };
        }

        return total;
    }
}
