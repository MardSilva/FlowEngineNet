using Flow.Documents;

namespace Flow.Layout;

/// <summary>Describes the host's broad device category without affecting canonical identity.</summary>
public enum DeviceClass
{
    Unknown,
    Phone,
    Tablet,
    Desktop,
    Print,
}

/// <summary>Selects continuous, paged, or print reading behavior.</summary>
public enum ReadingMode
{
    Flow,
    Paged,
    Print,
}

/// <summary>Contains noncanonical runtime inputs used to produce one layout.</summary>
public sealed record LayoutContext
{
    public LayoutContext(
        double viewportWidth,
        double viewportHeight,
        DeviceClass deviceClass = DeviceClass.Unknown,
        ReadingMode readingMode = ReadingMode.Flow,
        bool allowTwoColumns = false,
        UserReadingPreferences? userPreferences = null,
        RendererSafetyConstraints? rendererConstraints = null)
    {
        ValidateViewportDimension(viewportWidth, nameof(viewportWidth));
        ValidateViewportDimension(viewportHeight, nameof(viewportHeight));

        if (!Enum.IsDefined(deviceClass))
        {
            throw new ArgumentOutOfRangeException(nameof(deviceClass), deviceClass, "The device class is not defined.");
        }

        if (!Enum.IsDefined(readingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(readingMode), readingMode, "The reading mode is not defined.");
        }

        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        DeviceClass = deviceClass;
        ReadingMode = readingMode;
        AllowTwoColumns = allowTwoColumns;
        UserPreferences = userPreferences;
        RendererConstraints = rendererConstraints;
    }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public DeviceClass DeviceClass { get; }

    public ReadingMode ReadingMode { get; }

    public bool AllowTwoColumns { get; }

    public UserReadingPreferences? UserPreferences { get; }

    public RendererSafetyConstraints? RendererConstraints { get; }

    private static void ValidateViewportDimension(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A viewport dimension must be finite and greater than zero.");
        }
    }
}
