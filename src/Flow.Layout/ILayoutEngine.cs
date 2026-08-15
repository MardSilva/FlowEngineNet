using Flow.Documents;

namespace Flow.Layout;

/// <summary>Transforms semantic documents into renderer-independent layout decisions.</summary>
public interface ILayoutEngine
{
    /// <summary>Creates a layout without mutating the source document.</summary>
    /// <param name="document">A semantically valid Flow document.</param>
    /// <param name="context">Runtime viewport, reading mode, and reader state.</param>
    /// <returns>An immutable renderer-independent layout.</returns>
    public LayoutDocument Layout(FlowDocument document, LayoutContext context);
}
