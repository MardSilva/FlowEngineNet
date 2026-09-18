namespace Flow.Epub;

internal static class EpubMediaTypeClassifier
{
    internal static bool IsEmbeddedFont(string mediaType) => mediaType.Equals(
            "font/otf",
            StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("font/ttf", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("font/woff", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("font/woff2", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/vnd.ms-opentype", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/font-woff", StringComparison.OrdinalIgnoreCase);
}
