using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsWindowSizePolicyTests
{
    [Theory]
    [InlineData(96, 1000, 660)]
    [InlineData(144, 1500, 990)]
    [InlineData(192, 2000, 1320)]
    public void MinimumSizePreservesLogicalDimensionsAcrossDisplayScaling(
        uint dpi,
        int expectedWidth,
        int expectedHeight)
    {
        var result = FlowWindowsWindowSizePolicy.GetMinimumSize(dpi, 3000, 2000);

        Assert.Equal(new FlowWindowsPixelSize(expectedWidth, expectedHeight), result);
    }

    [Fact]
    public void MinimumSizeDoesNotExceedTheMonitorWorkArea()
    {
        var result = FlowWindowsWindowSizePolicy.GetMinimumSize(192, 1366, 728);

        Assert.Equal(new FlowWindowsPixelSize(1366, 728), result);
    }

    [Fact]
    public void InitialSizeUsesTheSameDpiAwarePolicy()
    {
        var result = FlowWindowsWindowSizePolicy.GetInitialSize(144, 2560, 1528);

        Assert.Equal(new FlowWindowsPixelSize(1680, 1080), result);
    }
}
