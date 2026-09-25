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
        RefreshButton.IsEnabled = viewModel.Settings.PersonalLibraryPath is not null;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
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
        RefreshButton.IsEnabled = true;
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
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ChooseFolderButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        BookList.IsEnabled = false;
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
            ScanProgress.Visibility = Visibility.Collapsed;
            ChooseFolderButton.IsEnabled = true;
            RefreshButton.IsEnabled = _viewModel.Settings.PersonalLibraryPath is not null;
            BookList.IsEnabled = true;
        }
    }

    private async void BookList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookList.SelectedItem is not LibraryDisplayItem selected)
        {
            DetailsCard.Visibility = Visibility.Collapsed;
            UpdateResponsiveLayout();
            return;
        }

        DetailsCard.Visibility = Visibility.Visible;
        UpdateResponsiveLayout();
        BookTitle.Text = selected.DisplayTitle;
        BookMetadata.Text = selected.DisplayMetadata;
        BookStatus.Text = selected.Item.IsUsable
            ? _viewModel.Text["LibraryUsable"]
            : _viewModel.Text["LibraryNeedsReview"];
        await SetCoverAsync(selected.Item.Summary);
    }

    private void UpdateResponsiveLayout()
    {
        var compact = ActualWidth < 720;
        var narrow = compact;
        var lowHeight = ActualHeight < 650;
        var showingCompactDetails = compact && BookList.SelectedItem is not null;

        LeadText.Visibility = showingCompactDetails ? Visibility.Collapsed : Visibility.Visible;
        FolderControlsGrid.Visibility = showingCompactDetails ? Visibility.Collapsed : Visibility.Visible;
        BackToBooksButton.Visibility = showingCompactDetails ? Visibility.Visible : Visibility.Collapsed;
        BookList.Visibility = showingCompactDetails ? Visibility.Collapsed : Visibility.Visible;
        FolderActionsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(FolderPathBox, narrow ? 3 : 1);
        Grid.SetColumn(ChooseFolderButton, narrow ? 1 : 1);
        Grid.SetRow(ChooseFolderButton, narrow ? 1 : 0);
        Grid.SetColumn(RefreshButton, narrow ? 2 : 2);
        Grid.SetRow(RefreshButton, narrow ? 1 : 0);
        ChooseFolderButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        RefreshButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        BookListColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
        BookDetailsColumn.Width = compact ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        BooksAndDetailsGrid.ColumnSpacing = compact ? 0 : 20;
        BooksAndDetailsGrid.RowSpacing = compact ? 12 : 0;

        if (showingCompactDetails)
        {
            BookListRow.Height = new GridLength(0);
            BookDetailsRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(DetailsCard, 0);
            Grid.SetRow(DetailsCard, 1);
            DetailsCard.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            BookListRow.Height = new GridLength(1, GridUnitType.Star);
            BookDetailsRow.Height = new GridLength(0);
            Grid.SetColumn(DetailsCard, 1);
            Grid.SetRow(DetailsCard, 0);
            DetailsCard.VerticalAlignment = VerticalAlignment.Top;
        }

        DetailsCard.Padding = new Thickness(lowHeight ? 12 : compact ? 16 : 20);
        DetailsContentGrid.ColumnSpacing = lowHeight ? 10 : compact ? 12 : 16;
        CoverImage.MaxWidth = lowHeight ? 56 : compact ? 120 : 180;
        CoverImage.MaxHeight = lowHeight ? 84 : compact ? 180 : 270;
    }

    private void BackToBooksButton_Click(object sender, RoutedEventArgs e)
    {
        BookList.SelectedItem = null;
        BookList.Focus(FocusState.Programmatic);
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
        BackToBooksButton.Content = text["LibraryBackToList"];
        InspectButton.Content = text["InspectAction"];
        ImportButton.Content = text["ImportAction"];
        ValidateButton.Content = text["ValidateAction"];
        PreviewButton.Content = text["PreviewAction"];
        AutomationProperties.SetName(BookList, text["LibraryListName"]);
        AutomationProperties.SetName(CoverImage, text["CoverLabel"]);
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
