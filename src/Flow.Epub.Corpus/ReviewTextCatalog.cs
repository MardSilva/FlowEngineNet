using System.Globalization;
using System.Resources;
using Flow.Rendering.Html;

namespace Flow.Epub.Corpus;

internal sealed class ReviewTextCatalog
{
    private static readonly ResourceManager Resources = new(
        "Flow.Epub.Corpus.Resources.ReviewMessages",
        typeof(ReviewTextCatalog).Assembly);

    private ReviewTextCatalog(CultureInfo culture)
    {
        Culture = culture;
    }

    internal CultureInfo Culture { get; }

    internal string LanguageTag => Culture.Name;

    internal string Get(string key) => Resources.GetString(key, Culture)
        ?? Resources.GetString(key, CultureInfo.GetCultureInfo("en-US"))
        ?? throw new MissingManifestResourceException($"Missing review resource '{key}'.");

    internal string Label(string token) =>
        Resources.GetString($"Label_{token}", Culture)
        ?? Resources.GetString($"Label_{token}", CultureInfo.GetCultureInfo("en-US"))
        ?? token;

    internal static ReviewTextCatalog For(HtmlBookUiLanguage requested, string? publicationLanguage)
    {
        var normalizedLanguage = publicationLanguage?.Replace('_', '-');
        var portuguese = requested == HtmlBookUiLanguage.PortugueseBrazil
                         || (requested == HtmlBookUiLanguage.Automatic
                             && normalizedLanguage is not null
                             && (normalizedLanguage.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
                                 || normalizedLanguage.StartsWith("pt-BR-", StringComparison.OrdinalIgnoreCase)));
        return new ReviewTextCatalog(CultureInfo.GetCultureInfo(portuguese ? "pt-BR" : "en-US"));
    }
}
