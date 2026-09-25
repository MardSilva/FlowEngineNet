namespace Flow.Windows.Shell;

public static class FlowWindowsWindowSizePolicy
{
    public const int InitialWidth = 1120;
    public const int InitialHeight = 720;
    public const int MinimumWidth = 1000;
    public const int MinimumHeight = 660;

    public static FlowWindowsPixelSize GetInitialSize(uint dpi, int workAreaWidth, int workAreaHeight) =>
        ScaleAndClamp(InitialWidth, InitialHeight, dpi, workAreaWidth, workAreaHeight);

    public static FlowWindowsPixelSize GetMinimumSize(uint dpi, int workAreaWidth, int workAreaHeight) =>
        ScaleAndClamp(MinimumWidth, MinimumHeight, dpi, workAreaWidth, workAreaHeight);

    private static FlowWindowsPixelSize ScaleAndClamp(
        int logicalWidth,
        int logicalHeight,
        uint dpi,
        int workAreaWidth,
        int workAreaHeight)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workAreaWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workAreaHeight);

        var width = checked((int)Math.Round(logicalWidth * dpi / 96d, MidpointRounding.AwayFromZero));
        var height = checked((int)Math.Round(logicalHeight * dpi / 96d, MidpointRounding.AwayFromZero));
        return new FlowWindowsPixelSize(
            Math.Min(width, workAreaWidth),
            Math.Min(height, workAreaHeight));
    }
}

public readonly record struct FlowWindowsPixelSize(int Width, int Height);
