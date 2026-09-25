using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Flow.Windows.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly FlowWindowsShellViewModel _viewModel;
    private readonly FlowWindowsLibraryService _library;
    private readonly Func<Task<string?>> _chooseFolder;
    private readonly Action<string, FlowWindowsOperationKind> _openOperation;
    private readonly Action<string> _openPreview;
    private CancellationTokenSource? _scanCancellation;

    public LibraryPage(
        FlowWindowsShellViewModel viewModel,
        FlowWindowsLibraryService library,
        Func<Task<string?>> chooseFolder,
        Action<string, FlowWindowsOperationKind> openOperation,
        Action<string> openPreview)
    {
        _viewModel = viewModel;
        _library = library;
        _chooseFolder = chooseFolder;
        _openOperation = openOperation;
        _openPreview = openPreview;
        InitializeComponent();
        Localize();
        FolderPathBox.Text = viewModel.Settings.PersonalLibraryPath ?? string.Empty;
        Loaded += async (_, _) =>
        {
            if (viewModel.Settings.PersonalLibraryPath is not null)
            {
                await RefreshAsync();
            }
        };
        Unloaded += (_, _) => _scanCancellation?.Cancel();
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await _chooseFolder();
        if (path is null)
        {
            return;
        }

        await _viewModel.SetPersonalLibraryPathAsync(path);
        FolderPathBox.Text = path;
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_viewModel.Settings.PersonalLibraryPath is not { } path)
        {
            return;
        }

        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        ProgressPanel.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        BookList.ItemsSource = null;
        DetailsCard.Visibility = Visibility.Collapsed;
        try
        {
            var progress = new Progress<(int Completed, int Total)>(value =>
            {
                ScanProgress.IsIndeterminate = value.Total == 0;
                ScanProgress.Maximum = Math.Max(1, value.Total);
                ScanProgress.Value = value.Completed;
                ScanStatus.Text = string.Format(_viewModel.Text["LibraryProgress"], value.Completed, value.Total);
            });
            var index = await _library.DiscoverAsync(path, progress, _scanCancellation.Token);
            BookList.ItemsSource = index.Items.Select(item => new LibraryDisplayItem(item)).ToArray();
            ScanStatus.Text = index.WasTruncated
                ? _viewModel.Text["LibraryTruncated"]
                : string.Format(_viewModel.Text["LibraryComplete"], index.Items.Length);
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = _viewModel.Text["LibraryCanceled"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ScanStatus.Text = exception.Message;
        }
        finally
        {
            ScanProgress.IsIndeterminate = false;
        }
    }

    private async void BookList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookList.SelectedItem is not LibraryDisplayItem selected)
        {
            DetailsCard.Visibility = Visibility.Collapsed;
            return;
        }

        DetailsCard.Visibility = Visibility.Visible;
        BookTitle.Text = selected.DisplayTitle;
        BookMetadata.Text = selected.DisplayMetadata;
        BookStatus.Text = selected.Item.IsUsable
            ? _viewModel.Text["LibraryUsable"]
            : _viewModel.Text["LibraryNeedsReview"];
        await SetCoverAsync(selected.Item.Summary);
    }

    private void InspectButton_Click(object sender, RoutedEventArgs e) => Open(FlowWindowsOperationKind.Inspect);

    private void ImportButton_Click(object sender, RoutedEventArgs e) => Open(FlowWindowsOperationKind.Import);

    private void ValidateButton_Click(object sender, RoutedEventArgs e) => Open(FlowWindowsOperationKind.Validate);

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (BookList.SelectedItem is LibraryDisplayItem selected)
        {
            _openPreview(selected.Item.SourcePath);
        }
    }

    private void Open(FlowWindowsOperationKind operation)
    {
        if (BookList.SelectedItem is LibraryDisplayItem selected)
        {
            _openOperation(selected.Item.SourcePath, operation);
        }
    }

    private async Task SetCoverAsync(FlowWindowsBookSummary? summary)
    {
        CoverImage.Source = null;
        if (summary is null || summary.CoverBytes.IsDefaultOrEmpty)
        {
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(summary.CoverBytes.ToArray());
            await writer.StoreAsync();
        }

        stream.Seek(0);
        if (string.Equals(summary.CoverMediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            var image = new SvgImageSource();
            await image.SetSourceAsync(stream);
            CoverImage.Source = image;
        }
        else
        {
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            CoverImage.Source = image;
        }
    }

    private void Localize()
    {
        var text = _viewModel.Text;
        TitleText.Text = text["LibraryTitle"];
        LeadText.Text = text["LibraryLead"];
        FolderPathBox.PlaceholderText = text["LibraryNoFolder"];
        ChooseFolderButton.Content = text["LibraryChooseFolder"];
        RefreshButton.Content = text["LibraryRefresh"];
        InspectButton.Content = text["InspectAction"];
        ImportButton.Content = text["ImportAction"];
        ValidateButton.Content = text["ValidateAction"];
        PreviewButton.Content = text["PreviewAction"];
        AutomationProperties.SetName(BookList, text["LibraryListName"]);
    }

    private sealed record LibraryDisplayItem(FlowWindowsLibraryItem Item)
    {
        public string DisplayTitle => Item.Summary?.Title ?? Path.GetFileNameWithoutExtension(Item.SourcePath);

        public string DisplayMetadata => Item.Summary is { } summary
            ? string.Join(" · ", new[] { summary.Authors.FirstOrDefault(), summary.Language, summary.EpubVersion }
                .Where(static value => !string.IsNullOrWhiteSpace(value)))
            : Path.GetFileName(Item.SourcePath);
    }
}
