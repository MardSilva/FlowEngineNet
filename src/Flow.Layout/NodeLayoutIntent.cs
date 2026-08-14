using Flow.Documents;

namespace Flow.Layout;

public abstract record NodeLayoutIntent;

public sealed record DefaultLayoutIntent : NodeLayoutIntent
{
    public static DefaultLayoutIntent Instance { get; } = new();

    private DefaultLayoutIntent()
    {
    }
}

public sealed record HeadingLayoutIntent(
    bool KeepWithNext,
    bool AvoidBreakAfter) : NodeLayoutIntent;

public sealed record FigureLayoutIntent(
    bool KeepWithCaption,
    Length MaximumWidth,
    bool ScaleDownToFit,
    PreferredPlacement PreferredPlacement) : NodeLayoutIntent;

public sealed record CodeBlockLayoutIntent(
    bool AvoidSplit,
    bool PreserveWhitespace) : NodeLayoutIntent;

public sealed record FootnoteLayoutIntent(
    FootnotePresentationMode PreferredPresentation) : NodeLayoutIntent;
