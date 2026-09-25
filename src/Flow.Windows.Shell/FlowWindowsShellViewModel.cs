using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Flow.Windows.Shell;

/// <summary>Owns navigation and reconstructible preferences for the WinUI shell.</summary>
public sealed class FlowWindowsShellViewModel : INotifyPropertyChanged
{
    private readonly IFlowWindowsSettingsStore _settingsStore;
    private FlowWindowsSettings _settings;
    private FlowWindowsDestination _destination;

    public FlowWindowsShellViewModel(
        IFlowWindowsSettingsStore settingsStore,
        FlowWindowsSettings initialSettings)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(initialSettings);
        _settingsStore = settingsStore;
        _settings = initialSettings;
        _destination = FlowWindowsDestination.Home;
        Text = new FlowWindowsTextCatalog(initialSettings.Language);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FlowWindowsSettings Settings => _settings;

    public FlowWindowsTextCatalog Text { get; private set; }

    public FlowWindowsDestination Destination
    {
        get => _destination;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetField(ref _destination, value);
        }
    }

    public async Task ApplySettingsAsync(
        string language,
        FlowWindowsTheme theme,
        bool advancedMode,
        CancellationToken cancellationToken = default)
    {
        await ApplySettingsAsync(language, theme, advancedMode, _settings.PersonalLibraryPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetPersonalLibraryPathAsync(
        string? path,
        CancellationToken cancellationToken = default) =>
        await ApplySettingsAsync(
                _settings.Language,
                _settings.Theme,
                _settings.AdvancedMode,
                path,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task ApplySettingsAsync(
        string language,
        FlowWindowsTheme theme,
        bool advancedMode,
        string? personalLibraryPath,
        CancellationToken cancellationToken)
    {
        var updated = new FlowWindowsSettings(language, theme, advancedMode, personalLibraryPath);
        if (updated == _settings)
        {
            return;
        }

        await _settingsStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        _settings = updated;
        Text = new FlowWindowsTextCatalog(updated.Language);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(Text));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
