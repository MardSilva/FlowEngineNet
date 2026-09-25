using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Flow.Windows.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly FlowWindowsShellViewModel _viewModel;
    private readonly Func<string, FlowWindowsTheme, bool, CancellationToken, Task> _apply;
    private bool _initialized;

    public SettingsPage(
        FlowWindowsShellViewModel viewModel,
        Func<string, FlowWindowsTheme, bool, CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(apply);
        _viewModel = viewModel;
        _apply = apply;
        InitializeComponent();
        Localize();
        LanguageSelector.SelectedIndex = viewModel.Settings.Language == FlowWindowsSettings.PortugueseBrazil ? 1 : 0;
        ThemeSelector.SelectedIndex = (int)viewModel.Settings.Theme;
        AdvancedModeToggle.IsOn = viewModel.Settings.AdvancedMode;
        _initialized = true;
    }

    private async void Setting_SelectionChanged(object sender, SelectionChangedEventArgs args) => await SaveAsync();

    private async void Setting_Toggled(object sender, RoutedEventArgs args) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (!_initialized
            || LanguageSelector.SelectedItem is not ComboBoxItem languageItem
            || ThemeSelector.SelectedItem is not ComboBoxItem themeItem
            || !Enum.TryParse<FlowWindowsTheme>(themeItem.Tag?.ToString(), out var theme))
        {
            return;
        }

        IsEnabled = false;
        try
        {
            await _apply(
                languageItem.Tag?.ToString() ?? FlowWindowsSettings.English,
                theme,
                AdvancedModeToggle.IsOn,
                CancellationToken.None);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Localize()
    {
        var text = _viewModel.Text;
        TitleText.Text = text["SettingsTitle"];
        LeadText.Text = text["SettingsLead"];
        LanguageLabel.Text = text["LanguageLabel"];
        ThemeLabel.Text = text["ThemeLabel"];
        SystemThemeItem.Content = text["ThemeSystem"];
        LightThemeItem.Content = text["ThemeLight"];
        DarkThemeItem.Content = text["ThemeDark"];
        AdvancedModeToggle.Header = text["AdvancedMode"];
        AdvancedDescription.Text = text["AdvancedModeDescription"];
        SaveStatus.Message = text["SettingsSaved"];
        AutomationProperties.SetName(LanguageSelector, text["LanguageLabel"]);
        AutomationProperties.SetName(ThemeSelector, text["ThemeLabel"]);
        AutomationProperties.SetHelpText(AdvancedModeToggle, text["AdvancedModeDescription"]);
    }
}
