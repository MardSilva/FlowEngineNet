using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsShellViewModelTests
{
    [Fact]
    public async Task NavigationAndSettingsAreTypedObservableAndPersisted()
    {
        var store = new MemorySettingsStore();
        var viewModel = new FlowWindowsShellViewModel(store, new FlowWindowsSettings());
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.Destination = FlowWindowsDestination.HowItWorks;
        await viewModel.ApplySettingsAsync(
            FlowWindowsSettings.PortugueseBrazil,
            FlowWindowsTheme.Dark,
            advancedMode: true);

        Assert.Equal(FlowWindowsDestination.HowItWorks, viewModel.Destination);
        Assert.Equal(FlowWindowsSettings.PortugueseBrazil, viewModel.Settings.Language);
        Assert.Equal("Como funciona", viewModel.Text["NavHowItWorks"]);
        Assert.Equal(viewModel.Settings, store.Saved);
        Assert.Contains(nameof(viewModel.Destination), changed);
        Assert.Contains(nameof(viewModel.Settings), changed);
        Assert.Contains(nameof(viewModel.Text), changed);
    }

    [Fact]
    public void EnglishAndPortugueseCatalogsHaveMatchingNonemptyKeys()
    {
        var english = new FlowWindowsTextCatalog(FlowWindowsSettings.English);
        var portuguese = new FlowWindowsTextCatalog(FlowWindowsSettings.PortugueseBrazil);

        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), portuguese.Keys.Order(StringComparer.Ordinal));
        Assert.All(english.Keys, key => Assert.False(string.IsNullOrWhiteSpace(english[key])));
        Assert.All(portuguese.Keys, key => Assert.False(string.IsNullOrWhiteSpace(portuguese[key])));
    }

    [Fact]
    public async Task ApplyingSameSettingsDoesNotRewriteStorage()
    {
        var store = new MemorySettingsStore();
        var settings = new FlowWindowsSettings();
        var viewModel = new FlowWindowsShellViewModel(store, settings);

        await viewModel.ApplySettingsAsync(settings.Language, settings.Theme, settings.AdvancedMode);

        Assert.Equal(0, store.SaveCount);
    }

    private sealed class MemorySettingsStore : IFlowWindowsSettingsStore
    {
        public FlowWindowsSettings? Saved { get; private set; }

        public int SaveCount { get; private set; }

        public Task<FlowWindowsSettings> LoadAsync(
            FlowWindowsSettings fallback,
            CancellationToken cancellationToken = default) => Task.FromResult(Saved ?? fallback);

        public Task SaveAsync(FlowWindowsSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
