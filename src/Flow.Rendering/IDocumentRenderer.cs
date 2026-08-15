using Flow.Documents;
using Flow.Layout;

namespace Flow.Rendering;

public interface IDocumentRenderer
{
    public RenderedDocument Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences);
}
