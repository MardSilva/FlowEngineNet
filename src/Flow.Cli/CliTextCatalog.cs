using System.Globalization;
using System.Resources;

namespace Flow.Cli;

/// <summary>Resolves localized, human-readable CLI text without changing stable command or diagnostic identifiers.</summary>
public sealed class CliTextCatalog
{
    public const string DefaultCultureName = "en-US";
    public const string PortugueseBrazilCultureName = "pt-BR";

    private static readonly ResourceManager Resources = new(
        "Flow.Cli.Resources.CliMessages",
        typeof(CliTextCatalog).Assembly);

    public CliTextCatalog(string? cultureName = null)
    {
        Culture = ResolveCulture(cultureName);
    }

    public CultureInfo Culture { get; }

    public string Get(string key) => Resources.GetString(key, Culture)
        ?? Resources.GetString(key, CultureInfo.GetCultureInfo(DefaultCultureName))
        ?? throw new MissingManifestResourceException($"Missing CLI resource '{key}'.");

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arguments);

    public string Diagnostic(string code, string key, params object?[] arguments) =>
        $"{code}: {Format(key, arguments)}";

    public string Severity(string value) => Get($"Severity_{value}");

    public string DiagnosticMessage(string code, string fallback)
    {
        if (Culture.Name.Equals(DefaultCultureName, StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var localized = Resources.GetString($"Diagnostic_{code}", Culture);
        if (string.IsNullOrWhiteSpace(localized))
        {
            return fallback;
        }

        return $"{localized} {Format("DiagnosticTechnicalDetail", fallback)}";
    }

    public static bool TryNormalizeCulture(string value, out string cultureName)
    {
        if (value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.Equals(DefaultCultureName, StringComparison.OrdinalIgnoreCase))
        {
            cultureName = DefaultCultureName;
            return true;
        }

        if (value.Equals("pt", StringComparison.OrdinalIgnoreCase)
            || value.Equals(PortugueseBrazilCultureName, StringComparison.OrdinalIgnoreCase))
        {
            cultureName = PortugueseBrazilCultureName;
            return true;
        }

        cultureName = DefaultCultureName;
        return false;
    }

    private static CultureInfo ResolveCulture(string? cultureName) =>
        TryNormalizeCulture(cultureName ?? DefaultCultureName, out var normalized)
            ? CultureInfo.GetCultureInfo(normalized)
            : CultureInfo.GetCultureInfo(DefaultCultureName);
}
