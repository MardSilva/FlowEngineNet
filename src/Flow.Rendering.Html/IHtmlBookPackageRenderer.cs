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

    /// <summary>Renders a deterministic HTML book package with explicit noncanonical package options.</summary>
    public HtmlBookPackage Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        HtmlBookIntegrity integrity,
        HtmlBookPackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UiLanguage != HtmlBookUiLanguage.Automatic)
        {
            throw new NotSupportedException("This HTML book renderer does not support explicit UI localization.");
        }

        return Render(document, layout, userPreferences, integrity);
    }
}
