using Flow.Documents;
using Flow.Layout;

namespace Flow.Rendering;

/// <summary>Renders a semantic document using its corresponding immutable layout.</summary>
public interface IDocumentRenderer
{
    /// <summary>Produces a renderer-specific artifact.</summary>
    /// <param name="document">The source semantic document.</param>
    /// <param name="layout">A layout produced for the same document identity and version.</param>
    /// <param name="userPreferences">The active noncanonical reader preferences.</param>
    /// <returns>The immutable rendered artifact.</returns>
    public RenderedDocument Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences);
}
