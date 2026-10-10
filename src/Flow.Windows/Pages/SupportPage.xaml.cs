using System.Globalization;
using System.Net;
using Flow.Support;
using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.Credentials;

namespace Flow.Windows.Pages;

public sealed partial class SupportPage : Page
{
    private const string Resource = "FlowEngineNet.SupporterKit";
    private const string Account = "flow";
    private readonly FlowWindowsTextCatalog _text;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PasswordVault _vault = new();
    private string? _key;
    private bool _busy;

    public SupportPage(FlowWindowsTextCatalog text)
    {
        _text = text;
        InitializeComponent();
        TitleText.Text = text["NavSupport"];
        LeadText.Text = text["SupportLead"];
        InstructionsText.Text = text["SupportInstructions"];
        KeyInput.Header = text["SupportKey"];
        LoginPrefixText.Text = text["SupportLoginPrefix"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LoginLink, text["SupportLogin"]);
        ManageLink.Content = text["SupportManage"];
        ConnectButton.Content = text["SupportConnect"];
        RefreshButton.Content = text["SupportRefresh"];
        DisconnectButton.Content = text["SupportDisconnect"];
        RecognitionText.Text = text["SupportDisconnected"];
        try
        {
            var credential = _vault.Retrieve(Resource, Account);
            credential.RetrievePassword();
            _key = credential.Password;
            RecognitionText.Text = text["SupportSaved"];
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A missing credential is expected on the first visit.
        }
        RefreshButton.IsEnabled = DisconnectButton.IsEnabled = _key is not null;
        PageScrollViewer.SizeChanged += (_, args) => UpdateContentWidth(args.NewSize.Width);
        Loaded += (_, _) => UpdateContentWidth(PageScrollViewer.ActualWidth);
        Unloaded += (_, _) => _lifetime.Cancel();
    }

    private void UpdateContentWidth(double width)
    {
        if (width > 0)
        {
            PageContent.Width = Math.Min(PageContent.MaxWidth, width);
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await global::Windows.System.Launcher.LaunchUriAsync(
                    new Uri(FlowSupporterClient.ServiceOrigin, "auth/patreon/start")))
            {
                ShowMessage(_key is null ? "SupportConnectUnavailable" : "SupportUnavailable", InfoBarSeverity.Warning);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowMessage("SupportUnavailable", InfoBarSeverity.Warning);
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyInput.Password.Trim();
        if (key.Length != 64 || !key.All(Uri.IsHexDigit))
        {
            ShowMessage("SupportInvalidKey", InfoBarSeverity.Warning);
            return;
        }
        await RefreshAsync(key, save: true);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_key is not null)
        {
            await RefreshAsync(_key, save: false);
        }
    }

    private async Task RefreshAsync(string key, bool save)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        ConnectButton.IsEnabled = RefreshButton.IsEnabled = DisconnectButton.IsEnabled = false;
        Progress.IsActive = true;
        Progress.Visibility = Visibility.Visible;
        Message.IsOpen = false;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            var status = await new FlowSupporterClient(http).GetStatusAsync(key, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (save)
            {
                _vault.Add(new PasswordCredential(Resource, Account, key));
            }
            _key = key;
            KeyInput.Password = string.Empty;
            var stateKey = status.CurrentStatus switch
            {
                "entitled" => "SupportActive",
                "free" => "SupportFree",
                "former" => "SupportFormer",
                "declined" => "SupportDeclined",
                _ => "SupportNone",
            };
            var tiers = status.TierIds.Select(id => id switch
            {
                "29777290" => _text["SupportTier3"],
                "29777294" => _text["SupportTier7"],
                "29777296" => _text["SupportTier15"],
                _ => _text["SupportActive"],
            }).Distinct();
            RecognitionText.Text = $"{status.DisplayName(_text.Language)}\n{_text[stateKey]}";
            if (status.CurrentStatus == "entitled")
            {
                RecognitionText.Text += "\n" + string.Join(", ", tiers);
                if (status.CurrentSupportStartedAt is { } started)
                {
                    RecognitionText.Text += "\n" + FormatDuration(started);
                }
            }
            if (status.IsStale)
            {
                ShowMessage("SupportStale", InfoBarSeverity.Warning);
            }
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            ShowMessage("SupportUnauthorized", InfoBarSeverity.Warning);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            or InvalidDataException or System.Text.Json.JsonException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                ShowMessage(_key is null ? "SupportConnectUnavailable" : "SupportUnavailable", InfoBarSeverity.Warning);
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowMessage("SupportStorageFailed", InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            ConnectButton.IsEnabled = true;
            RefreshButton.IsEnabled = DisconnectButton.IsEnabled = _key is not null;
            Progress.IsActive = false;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private string FormatDuration(DateTimeOffset started)
    {
        var now = DateTimeOffset.UtcNow;
        var months = Math.Max(0, (now.Year - started.Year) * 12 + now.Month - started.Month);
        if (started.AddMonths(months) > now && months > 0)
        {
            months--;
        }
        var days = Math.Max(0, (now - started.AddMonths(months)).Days);
        var pt = _text.Language == FlowWindowsSettings.PortugueseBrazil;
        string Unit(int value, string singular, string plural) => $"{value} {(value == 1 ? singular : plural)}";
        if (months >= 12)
        {
            var years = Unit(months / 12, pt ? "ano" : "year", pt ? "anos" : "years");
            return months % 12 == 0 ? years : years + ", " + Unit(months % 12, pt ? "mês" : "month", pt ? "meses" : "months");
        }
        if (months > 0)
        {
            var label = Unit(months, pt ? "mês" : "month", pt ? "meses" : "months");
            return days == 0 ? label : label + ", " + Unit(days, pt ? "dia" : "day", pt ? "dias" : "days");
        }
        return days < 7 ? _text["SupportNew"] : string.Format(CultureInfo.CurrentCulture, _text["SupportDays"], days);
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_key is not null)
            {
                _vault.Remove(_vault.Retrieve(Resource, Account));
            }
            _key = null;
            KeyInput.Password = string.Empty;
            RecognitionText.Text = _text["SupportDisconnected"];
            RefreshButton.IsEnabled = DisconnectButton.IsEnabled = false;
            Message.IsOpen = false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowMessage("SupportStorageFailed", InfoBarSeverity.Error);
        }
    }

    private void ShowMessage(string key, InfoBarSeverity severity)
    {
        Message.Message = _text[key];
        Message.Severity = severity;
        Message.IsOpen = true;
    }
}
