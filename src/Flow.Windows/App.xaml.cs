using System.Globalization;
using Flow.Windows.Shell;
using Microsoft.UI.Xaml;

namespace Flow.Windows;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var localData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowEngineNet");
        var store = new JsonFlowWindowsSettingsStore(localData);
        var fallback = FlowWindowsSettings.DefaultForCulture(CultureInfo.CurrentUICulture.Name);
        var settings = await store.LoadAsync(fallback);
        var viewModel = new FlowWindowsShellViewModel(store, settings);
        _window = new MainWindow(viewModel, FlowWindowsOperationService.CreateDefault());
        _window.Activate();
    }
}
