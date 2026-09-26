using System.Runtime.InteropServices;
using Flow.Updates;
using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flow.Windows.Pages;

public sealed partial class AboutPage : Page
{
    private readonly FlowWindowsTextCatalog _text;
    private CancellationTokenSource? _updateCancellation;

    public AboutPage(FlowWindowsTextCatalog text)
    {
        _text = text;
        InitializeComponent();
        var identity = FlowWindowsProductIdentity.Resolve(
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), ReadManifest());
        TitleText.Text = text["NavAbout"];
        LeadText.Text = text["AboutLead"];
        IdentityText.Text = $"{text["AboutVersion"]}: {identity.Version}\n{text["AboutArchitecture"]}: {identity.Architecture}";
        if (identity.Revision is not null)
        {
            IdentityText.Text += $"\n{text["AboutRevision"]}: {identity.Revision}";
        }

        DistributionText.Text = text[$"About{identity.State}"];
        ModifiedText.Text = text["AboutModified"];
        ModifiedText.Visibility = identity.ModifiedSource ? Visibility.Visible : Visibility.Collapsed;
        UpdatesTitle.Text = text["UpdatesTitle"];
        UpdatesLead.Text = text["UpdatesLead"];
        PrereleaseOption.Content = text["UpdatesPrerelease"];
        CheckUpdatesButton.Content = text["UpdatesCheck"];
        CancelUpdateButton.Content = text["UpdatesCancel"];
        UpdateReleaseLink.Content = text["UpdatesOpenRelease"];
        Unloaded += (_, _) => _updateCancellation?.Cancel();
        LinksTitle.Text = text["AboutLinks"];
        LinksLead.Text = text["AboutLinksLead"];
        ProjectLink.Content = text["AboutProject"];
        SupportLink.Content = text["AboutSupport"];
        ReleasesLink.Content = text["AboutReleases"];
        LicensesLead.Text = text["AboutLicensesLead"];
        LicensesPanel.Header = text["AboutLicenses"];
        var licenses = ReadLocalText(Path.Combine(AppContext.BaseDirectory, "Assets", "LICENSES.txt"), 2 * 1024 * 1024);
        LicensesText.Text = string.IsNullOrWhiteSpace(licenses) ? text["AboutLicenseUnavailable"] : licenses;
        PageScrollViewer.SizeChanged += (_, args) => UpdateContentWidth(args.NewSize.Width);
        Loaded += (_, _) => UpdateContentWidth(PageScrollViewer.ActualWidth);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCancellation is not null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        CheckUpdatesButton.IsEnabled = false;
        PrereleaseOption.IsEnabled = false;
        CancelUpdateButton.IsEnabled = true;
        UpdateProgress.IsActive = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateReleaseLink.Visibility = Visibility.Collapsed;
        UpdateReleaseLink.NavigateUri = null;
        UpdateInstructions.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = _text["UpdatesChecking"];
        try
        {
            using var client = new FlowUpdateClient(FlowWindowsProductIdentity.PublicVersion, graphicalApplication: true);
            var result = await client.CheckAsync(
                PrereleaseOption.IsChecked == true ? UpdateChannel.Prerelease : UpdateChannel.Stable,
                _text.Language,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            UpdateStatusText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                _text[result.Status == FlowUpdateStatus.Available ? "UpdatesAvailable" : "UpdatesCurrent"],
                result.CurrentVersion, result.LatestVersion);
            UpdateReleaseLink.NavigateUri = result.ReleaseUrl;
            UpdateReleaseLink.Visibility = Visibility.Visible;
            UpdateInstructions.Text = _text[result.InstallationMethod == FlowInstallationMethod.Msi
                ? "UpdatesMsi" : "UpdatesManual"];
            UpdateInstructions.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = _text["UpdatesCancelled"];
        }
        catch (UpdateCheckException exception)
        {
            UpdateStatusText.Text = _text[$"Updates{exception.Kind}"];
        }
        finally
        {
            _updateCancellation = null;
            CheckUpdatesButton.IsEnabled = true;
            PrereleaseOption.IsEnabled = true;
            CancelUpdateButton.IsEnabled = false;
            UpdateProgress.IsActive = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => _updateCancellation?.Cancel();

    private void UpdateContentWidth(double availableWidth)
    {
        if (availableWidth > 0)
        {
            PageContent.Width = Math.Min(PageContent.MaxWidth, availableWidth);
        }
    }

    private static string? ReadManifest()
    {
        // The combined distribution places VERSION.json beside app/, not inside it.
        var manifest = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "VERSION.json"));
        return ReadLocalText(manifest, 16384);
    }

    private static string? ReadLocalText(string path, int limit)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            if (stream.Length > limit)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            var buffer = new char[limit + 1];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count > limit ? string.Empty : new string(buffer, 0, count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
