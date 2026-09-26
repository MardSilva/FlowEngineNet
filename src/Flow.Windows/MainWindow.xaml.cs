using Flow.Windows.Pages;
using Flow.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace Flow.Windows;

public sealed partial class MainWindow : Window
{
    private readonly FlowWindowsShellViewModel _viewModel;
    private readonly IFlowWindowsOperationService _operations;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly NativeWindowSizing _windowSizing;
    private FlowWindowsOperationKind _initialOperation = FlowWindowsOperationKind.Inspect;
    private string? _initialSourcePath;
    private string? _previewSourcePath;

    public MainWindow(
        FlowWindowsShellViewModel viewModel,
        IFlowWindowsOperationService operations)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(operations);
        _viewModel = viewModel;
        _operations = operations;
        InitializeComponent();
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowSizing = new NativeWindowSizing(windowHandle);
        AppWindow.Resize(_windowSizing.AttachAndGetInitialSize());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "flow.ico"));
        Root.ActualThemeChanged += (_, _) => UpdateLogo();
        ApplySettings();
        LocalizeShell();
        Navigation.SelectedItem = HomeItem;
        ShowDestination(FlowWindowsDestination.Home);
        Activated += (_, _) => HomeItem.Focus(FocusState.Programmatic);
        Closed += (_, _) => _windowSizing.Dispose();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag
            || !Enum.TryParse<FlowWindowsDestination>(tag, out var destination))
        {
            return;
        }

        _viewModel.Destination = destination;
        ShowDestination(destination);
    }

    private void ShowDestination(FlowWindowsDestination destination)
    {
        ContentFrame.Content = destination switch
        {
            FlowWindowsDestination.Home => new HomePage(_viewModel.Text, OpenOperation),
            FlowWindowsDestination.Library => new LibraryPage(
                _viewModel,
                new FlowWindowsLibraryService(_operations),
                ChooseFolderAsync,
                OpenOperation,
                OpenPreview),
            FlowWindowsDestination.Operations => new OperationsPage(
                _viewModel.Text,
                _operations,
                ChooseDocumentAsync,
                _initialOperation,
                _initialSourcePath,
                _viewModel.Settings.AdvancedMode,
                OpenPreview,
                SaveTextAsync,
                OpenPowerShellAsync),
            FlowWindowsDestination.Preview when _previewSourcePath is not null =>
                new PreviewPage(_viewModel.Text, _operations, _previewSourcePath),
            FlowWindowsDestination.HowItWorks => new HowItWorksPage(_viewModel.Text),
            FlowWindowsDestination.About => new AboutPage(_viewModel.Text),
            FlowWindowsDestination.Settings => new SettingsPage(_viewModel, ApplySettingsAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(destination)),
        };
    }

    private async Task ApplySettingsAsync(
        string language,
        FlowWindowsTheme theme,
        bool advancedMode,
        CancellationToken cancellationToken)
    {
        await _viewModel.ApplySettingsAsync(language, theme, advancedMode, cancellationToken);
        ApplySettings();
        LocalizeShell();
        ShowDestination(_viewModel.Destination);
    }

    private void ApplySettings()
    {
        Root.RequestedTheme = _viewModel.Settings.Theme switch
        {
            FlowWindowsTheme.Light => ElementTheme.Light,
            FlowWindowsTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateLogo();
    }

    private void LocalizeShell()
    {
        Title = _viewModel.Text["WindowTitle"];
        BrandText.Text = _viewModel.Text["AppName"];
        HomeItem.Content = _viewModel.Text["NavHome"];
        LibraryItem.Content = _viewModel.Text["NavLibrary"];
        OperationsItem.Content = _viewModel.Text["NavOperations"];
        HowItem.Content = _viewModel.Text["NavHowItWorks"];
        SettingsItem.Content = _viewModel.Text["NavSettings"];
        AboutItem.Content = _viewModel.Text["NavAbout"];
        AutomationProperties.SetName(Navigation, _viewModel.Text["NavigationName"]);
        AutomationProperties.SetName(ContentFrame, _viewModel.Text[_viewModel.Destination switch
        {
            FlowWindowsDestination.Home => "HomeTitle",
            FlowWindowsDestination.Library => "LibraryTitle",
            FlowWindowsDestination.Operations => "OperationsTitle",
            FlowWindowsDestination.Preview => "PreviewTitle",
            FlowWindowsDestination.HowItWorks => "HowTitle",
            FlowWindowsDestination.About => "NavAbout",
            _ => "SettingsTitle",
        }]);
    }

    private void OpenOperation(FlowWindowsOperationKind operation)
    {
        _initialSourcePath = null;
        _initialOperation = operation;
        _viewModel.Destination = FlowWindowsDestination.Operations;
        Navigation.SelectedItem = OperationsItem;
        ShowDestination(FlowWindowsDestination.Operations);
    }

    private void OpenOperation(string sourcePath, FlowWindowsOperationKind operation)
    {
        _initialSourcePath = sourcePath;
        _initialOperation = operation;
        _viewModel.Destination = FlowWindowsDestination.Operations;
        Navigation.SelectedItem = OperationsItem;
        ShowDestination(FlowWindowsDestination.Operations);
    }

    private void OpenPreview(string sourcePath)
    {
        _previewSourcePath = sourcePath;
        _viewModel.Destination = FlowWindowsDestination.Preview;
        Navigation.SelectedItem = null;
        ShowDestination(FlowWindowsDestination.Preview);
    }

    private Task<string?> ChooseDocumentAsync()
    {
        using var picker = new System.Windows.Forms.OpenFileDialog
        {
            Title = _viewModel.Text["EpubPickerTitle"],
            Filter = $"{_viewModel.Text["EpubFileFilter"]}|*.epub",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            AddExtension = true,
            DefaultExt = "epub",
        };
        var owner = new NativeWindowOwner(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var result = picker.ShowDialog(owner);
        return Task.FromResult(result == System.Windows.Forms.DialogResult.OK ? picker.FileName : null);
    }

    private async Task<string?> ChooseFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> SaveTextAsync(string suggestedName, string content)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };
        picker.FileTypeChoices.Add("Text log", [Path.GetExtension(suggestedName)]);
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await File.WriteAllTextAsync(file.Path, content, new System.Text.UTF8Encoding(false));
        return file.Path;
    }

    private static Task OpenPowerShellAsync()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }

    private void UpdateLogo()
    {
        var asset = _accessibility.HighContrast
            ? "flow-app-tile-512.png"
            : Root.ActualTheme == ElementTheme.Dark
                ? "flow-app-symbol-on-dark-512.png"
                : "flow-app-symbol-on-light-512.png";
        BrandImage.Source = new BitmapImage(new Uri($"ms-appx:///Assets/{asset}"));
        AutomationProperties.SetName(BrandImage, _viewModel.Text["AppName"]);
    }

    private sealed record NativeWindowOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
}
