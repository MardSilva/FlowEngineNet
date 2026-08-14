using Flow.Documents;

namespace Flow.Layout;

public interface ILayoutEngine
{
    public LayoutDocument Layout(FlowDocument document, LayoutContext context);
}
