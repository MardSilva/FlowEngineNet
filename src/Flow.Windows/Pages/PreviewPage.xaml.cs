using Flow.Application;
using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace Flow.Windows.Pages;

public sealed partial class PreviewPage : Page
{
    private const string PreviewHost = "flow-preview.local";
    private readonly FlowWindowsTextCatalog _text;
    private readonly IFlowWindowsOperationService _operations;
    private readonly string _sourcePath;
    private FlowWindowsPreviewSession? _session;
    private CancellationTokenSource? _cancellation;

    public PreviewPage(
        FlowWindowsTextCatalog text,
        IFlowWindowsOperationService operations,
        string sourcePath)
    {
        _text = text;
        _operations = operations;
        _sourcePath = sourcePath;
        InitializeComponent();
        Localize();
        Loaded += async (_, _) => await LoadPreviewAsync(SelectInitialProfile(ActualWidth));
        Unloaded += async (_, _) =>
        {
            _cancellation?.Cancel();
            PreviewWebView.Close();
            await DisposeSessionAsync();
        };
    }

    private async void PhoneButton_Click(object sender, RoutedEventArgs e) =>
        await LoadPreviewAsync(FlowWindowsPreviewProfile.Phone);

    private async void TabletButton_Click(object sender, RoutedEventArgs e) =>
        await LoadPreviewAsync(FlowWindowsPreviewProfile.Tablet);

    private async void DesktopButton_Click(object sender, RoutedEventArgs e) =>
        await LoadPreviewAsync(FlowWindowsPreviewProfile.Desktop);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private async Task LoadPreviewAsync(FlowWindowsPreviewProfile profile)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        PreviewWebView.CoreWebView2?.ClearVirtualHostNameToFolderMapping(PreviewHost);
        await DisposeSessionAsync();
        SetBusy(true);
        try
        {
            var progress = new Progress<FlowApplicationProgress>(value =>
                PreviewStatus.Message = _text[$"Progress{value.Stage}"]);
            _session = await _operations.CreatePreviewAsync(
                _sourcePath,
                profile,
                _text.Language == FlowWindowsSettings.PortugueseBrazil
                    ? FlowWindowsHtmlLanguage.PortugueseBrazil
                    : FlowWindowsHtmlLanguage.English,
                progress,
                _cancellation.Token);
            await ConfigureWebViewAsync(_session);
            SetProfileSize(profile);
            PreviewStatus.Message = _text["PreviewReady"];
            PreviewStatus.Severity = InfoBarSeverity.Success;
            PreviewStatus.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            PreviewStatus.Message = _text["ResultCanceled"];
            PreviewStatus.Severity = InfoBarSeverity.Warning;
            PreviewStatus.IsOpen = true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or NotSupportedException)
        {
            PreviewStatus.Message = $"{_text["PreviewFailed"]} {exception.Message}";
            PreviewStatus.Severity = InfoBarSeverity.Error;
            PreviewStatus.IsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConfigureWebViewAsync(FlowWindowsPreviewSession session)
    {
        await PreviewWebView.EnsureCoreWebView2Async();
        var core = PreviewWebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.ClearVirtualHostNameToFolderMapping(PreviewHost);
        core.SetVirtualHostNameToFolderMapping(
            PreviewHost,
            session.RootPath,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested -= Core_NewWindowRequested;
        core.NewWindowRequested += Core_NewWindowRequested;
        core.DownloadStarting -= Core_DownloadStarting;
        core.DownloadStarting += Core_DownloadStarting;
        core.PermissionRequested -= Core_PermissionRequested;
        core.PermissionRequested += Core_PermissionRequested;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested -= Core_WebResourceRequested;
        core.WebResourceRequested += Core_WebResourceRequested;
        PreviewWebView.Source = new Uri($"https://{PreviewHost}/index.html");
    }

    private void PreviewWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!IsPreviewUri(args.Uri))
        {
            args.Cancel = true;
            PreviewStatus.Message = _text["PreviewBlockedNavigation"];
            PreviewStatus.Severity = InfoBarSeverity.Warning;
            PreviewStatus.IsOpen = true;
        }
    }

    private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) =>
        args.Handled = true;

    private void Core_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Cancel = true;
        PreviewStatus.Message = _text["PreviewBlockedDownload"];
        PreviewStatus.Severity = InfoBarSeverity.Warning;
        PreviewStatus.IsOpen = true;
    }

    private static void Core_PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.State = CoreWebView2PermissionState.Deny;
        args.Handled = true;
    }

    private void Core_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (IsPreviewUri(args.Request.Uri))
        {
            return;
        }

        args.Response = sender.Environment.CreateWebResourceResponse(
            new InMemoryRandomAccessStream(),
            403,
            "Blocked by Flow preview",
            "Content-Type: text/plain");
    }

    private static bool IsPreviewUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, PreviewHost, StringComparison.OrdinalIgnoreCase);

    private static FlowWindowsPreviewProfile SelectInitialProfile(double availableWidth) =>
        availableWidth switch
        {
            >= 1180 => FlowWindowsPreviewProfile.Desktop,
            >= 900 => FlowWindowsPreviewProfile.Tablet,
            _ => FlowWindowsPreviewProfile.Phone,
        };

    private void SetProfileSize(FlowWindowsPreviewProfile profile)
    {
        (DeviceFrame.Width, DeviceFrame.Height) = profile switch
        {
            FlowWindowsPreviewProfile.Phone => (390, 700),
            FlowWindowsPreviewProfile.Tablet => (820, 760),
            _ => (1100, 760),
        };
        PreviewWebView.Width = DeviceFrame.Width;
        PreviewWebView.Height = DeviceFrame.Height;
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }
    }

    private void SetBusy(bool busy)
    {
        PreviewProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PhoneButton.IsEnabled = !busy;
        TabletButton.IsEnabled = !busy;
        DesktopButton.IsEnabled = !busy;
    }

    private void Localize()
    {
        TitleText.Text = _text["PreviewTitle"];
        NoticeText.Text = _text["PreviewNotice"];
        PhoneButton.Content = _text["PreviewPhone"];
        TabletButton.Content = _text["PreviewTablet"];
        DesktopButton.Content = _text["PreviewDesktop"];
        CancelButton.Content = _text["CancelAction"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            PreviewWebView,
            _text["PreviewSurface"]);
    }
}
