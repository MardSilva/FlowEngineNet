using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsSettingsTests
{
    [Fact]
    public async Task SettingsRoundTripPreservesLanguageThemeAndAdvancedMode()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonFlowWindowsSettingsStore(directory);
            var expected = new FlowWindowsSettings(
                FlowWindowsSettings.PortugueseBrazil,
                FlowWindowsTheme.Dark,
                advancedMode: true);

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync(new FlowWindowsSettings());

            Assert.Equal(expected, actual);
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), path => path.EndsWith(".tmp", StringComparison.Ordinal));
            Assert.False((await File.ReadAllTextAsync(store.SettingsPath)).StartsWith('\uFEFF'));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingInvalidOrOversizedSettingsUseReconstructibleFallback()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonFlowWindowsSettingsStore(directory);
            var fallback = new FlowWindowsSettings(FlowWindowsSettings.PortugueseBrazil);
            Assert.Equal(fallback, await store.LoadAsync(fallback));

            await File.WriteAllTextAsync(store.SettingsPath, "{invalid");
            Assert.Equal(fallback, await store.LoadAsync(fallback));

            await File.WriteAllBytesAsync(store.SettingsPath, new byte[20 * 1024]);
            Assert.Equal(fallback, await store.LoadAsync(fallback));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "en-US")]
    public void CultureFallbackSupportsOnlyDeclaredCatalogs(string culture, string expected)
    {
        Assert.Equal(expected, FlowWindowsSettings.DefaultForCulture(culture).Language);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flow-windows-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
