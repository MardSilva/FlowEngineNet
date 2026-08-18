using Flow.Documents;
using Flow.Layout;

namespace Flow.Rendering.Html;

/// <summary>Produces a multi-file HTML book without changing semantic content.</summary>
public interface IHtmlBookPackageRenderer
{
    /// <summary>Renders a deterministic HTML book package for one validated Flow layout.</summary>
    public HtmlBookPackage Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        HtmlBookIntegrity integrity);
}
