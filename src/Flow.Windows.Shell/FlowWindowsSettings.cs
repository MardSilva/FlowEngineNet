namespace Flow.Windows.Shell;

/// <summary>Selects the appearance of the Flow Windows application.</summary>
public enum FlowWindowsTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Identifies one page in the application shell.</summary>
public enum FlowWindowsDestination
{
    Home,
    Library,
    Operations,
    Preview,
    HowItWorks,
    Settings,
}

/// <summary>Contains small, local, and reconstructible shell preferences.</summary>
public sealed record FlowWindowsSettings
{
    public const string English = "en-US";
    public const string PortugueseBrazil = "pt-BR";

    public FlowWindowsSettings(
        string language = English,
        FlowWindowsTheme theme = FlowWindowsTheme.System,
        bool advancedMode = false,
        string? personalLibraryPath = null)
    {
        if (!IsSupportedLanguage(language))
        {
            throw new ArgumentException("The application language must be en-US or pt-BR.", nameof(language));
        }

        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        Language = language;
        Theme = theme;
        AdvancedMode = advancedMode;
        PersonalLibraryPath = NormalizeOptionalDirectory(personalLibraryPath);
    }

    public string Language { get; }

    public FlowWindowsTheme Theme { get; }

    public bool AdvancedMode { get; }

    /// <summary>Gets the optional local folder selected by the user. It is never canonical document data.</summary>
    public string? PersonalLibraryPath { get; }

    public static bool IsSupportedLanguage(string? value) =>
        string.Equals(value, English, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, PortugueseBrazil, StringComparison.OrdinalIgnoreCase);

    public static FlowWindowsSettings DefaultForCulture(string? cultureName) =>
        new(string.Equals(cultureName, PortugueseBrazil, StringComparison.OrdinalIgnoreCase)
            ? PortugueseBrazil
            : English);

    private static string? NormalizeOptionalDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("The personal library path must be absolute.", nameof(value));
        }

        return Path.GetFullPath(value);
    }
}
