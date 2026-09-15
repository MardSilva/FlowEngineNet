namespace Flow.Rendering.Html;

/// <summary>Selects the language used only by generated HTML book interface text.</summary>
public enum HtmlBookUiLanguage
{
    /// <summary>Uses the publication language when supported and English otherwise.</summary>
    Automatic,

    /// <summary>Uses English interface text.</summary>
    English,

    /// <summary>Uses European Portuguese interface text.</summary>
    PortuguesePortugal,

    /// <summary>Uses Brazilian Portuguese interface text.</summary>
    PortugueseBrazil,
}

/// <summary>Controls noncanonical HTML book package behavior.</summary>
public sealed record HtmlBookPackageOptions
{
    /// <summary>Creates package options.</summary>
    public HtmlBookPackageOptions(HtmlBookUiLanguage uiLanguage = HtmlBookUiLanguage.Automatic)
    {
        if (!Enum.IsDefined(uiLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(uiLanguage), uiLanguage, "The HTML book UI language is not defined.");
        }

        UiLanguage = uiLanguage;
    }

    /// <summary>Gets the language used by renderer-generated navigation and controls.</summary>
    public HtmlBookUiLanguage UiLanguage { get; }
}
