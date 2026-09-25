using Flow.Application;
using Flow.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Flow.Windows.Pages;

public sealed partial class OperationsPage : Page
{
    private readonly FlowWindowsTextCatalog _text;
    private readonly IFlowWindowsOperationService _operations;
    private readonly Func<Task<string?>> _chooseFile;
    private CancellationTokenSource? _cancellation;
    private string? _sourcePath;
    private FlowWindowsOperationKind _selectedOperation;

    public OperationsPage(
        FlowWindowsTextCatalog text,
        IFlowWindowsOperationService operations,
        Func<Task<string?>> chooseFile,
        FlowWindowsOperationKind initialOperation = FlowWindowsOperationKind.Inspect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(chooseFile);
        _text = text;
        _operations = operations;
        _chooseFile = chooseFile;
        _selectedOperation = initialOperation;
        InitializeComponent();
        Localize();
        SelectOperation(initialOperation);
        SetSource(null);
    }

    private async void ChooseButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await _chooseFile();
        if (path is not null)
        {
            SetSource(path);
        }
    }

    private void DropArea_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _text["DropFile"];
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            ShowInvalidSelection();
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 1 && items[0] is StorageFile file && IsSupportedPath(file.Path))
        {
            SetSource(file.Path);
            return;
        }

        ShowInvalidSelection();
    }

    private void InspectButton_Click(object sender, RoutedEventArgs e) => SelectOperation(FlowWindowsOperationKind.Inspect);

    private void ImportButton_Click(object sender, RoutedEventArgs e) => SelectOperation(FlowWindowsOperationKind.Import);

    private void ValidateButton_Click(object sender, RoutedEventArgs e) => SelectOperation(FlowWindowsOperationKind.Validate);

    private void HtmlBookCheck_Changed(object sender, RoutedEventArgs e) =>
        HtmlLanguagePanel.Visibility = HtmlBookCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await ExecuteAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void SelectOperation(FlowWindowsOperationKind operation)
    {
        _selectedOperation = operation;
        InspectButton.Style = operation == FlowWindowsOperationKind.Inspect
            ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
            : null;
        ImportButton.Style = operation == FlowWindowsOperationKind.Import
            ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
            : null;
        ValidateButton.Style = operation == FlowWindowsOperationKind.Validate
            ? Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style
            : null;
        ImportOptions.Visibility = operation == FlowWindowsOperationKind.Import ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSource(string? path)
    {
        _sourcePath = path;
        SourcePathText.Text = path ?? _text["NoFileSelected"];
        if (path is not null)
        {
            OutputPathBox.Text = _operations.SuggestDocumentOutputPath(path);
        }

        ResultBar.IsOpen = false;
        SummaryCard.Visibility = Visibility.Collapsed;
        DiagnosticsExpander.Visibility = Visibility.Collapsed;
    }

    private async Task ExecuteAsync()
    {
        if (_sourcePath is null || !IsOperationSupported(_sourcePath, _selectedOperation))
        {
            ShowInvalidSelection();
            return;
        }

        var policy = FlowWindowsOutputPolicy.RejectExisting;
        string? output = null;
        if (_selectedOperation == FlowWindowsOperationKind.Import)
        {
            if (string.IsNullOrWhiteSpace(OutputPathBox.Text))
            {
                ShowInvalidSelection();
                return;
            }

            try
            {
                output = Path.GetFullPath(OutputPathBox.Text);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                ShowResultMessage(_text["ResultUnexpected"], InfoBarSeverity.Error, exception.Message);
                return;
            }
        }

        if (output is not null)
        {
            var destinations = new List<(string Path, bool Directory)> { (output, false) };
            if (DiagnosticsCheck.IsChecked == true)
            {
                destinations.Add((Path.ChangeExtension(output, ".diagnostics.json"), false));
            }

            if (HtmlBookCheck.IsChecked == true)
            {
                destinations.Add((Path.Combine(
                    Path.GetDirectoryName(output)!,
                    Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(output)) + "_book"), true));
            }

            var finalExists = destinations.Any(destination => destination.Directory
                ? Directory.Exists(destination.Path)
                : File.Exists(destination.Path));
            var partialExists = destinations.Any(destination => destination.Directory
                ? Directory.Exists(_operations.GetInterruptedOutputPath(destination.Path))
                : File.Exists(_operations.GetInterruptedOutputPath(destination.Path)));
            if (finalExists)
            {
                if (!await ConfirmAsync(_text["ConfirmReplaceTitle"], _text["ConfirmReplaceText"]))
                {
                    return;
                }

                policy = FlowWindowsOutputPolicy.ReplaceConfirmed;
            }
            else if (partialExists)
            {
                if (!await ConfirmAsync(_text["ConfirmResumeTitle"], _text["ConfirmResumeText"]))
                {
                    return;
                }

                policy = FlowWindowsOutputPolicy.ResumeInterrupted;
            }
        }

        if (HtmlLanguageSelector.SelectedItem is not ComboBoxItem languageItem
            || !Enum.TryParse<FlowWindowsHtmlLanguage>(languageItem.Tag?.ToString(), out var htmlLanguage))
        {
            htmlLanguage = FlowWindowsHtmlLanguage.Automatic;
        }

        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<FlowApplicationProgress>(ShowProgress);
        try
        {
            var result = await _operations.ExecuteAsync(
                new FlowWindowsOperationRequest(
                    _selectedOperation,
                    _sourcePath,
                    output,
                    DiagnosticsCheck.IsChecked == true,
                    HtmlBookCheck.IsChecked == true,
                    htmlLanguage,
                    policy),
                progress,
                _cancellation.Token);
            await ShowResultAsync(result);
        }
        catch (OperationCanceledException)
        {
            ShowResultMessage(_text["ResultCanceled"], InfoBarSeverity.Warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or NotSupportedException or InvalidDataException
                                         or System.Text.Json.JsonException or ArgumentException)
        {
            ShowResultMessage(_text["ResultUnexpected"], InfoBarSeverity.Error, exception.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void ShowProgress(FlowApplicationProgress progress)
    {
        ProgressText.Text = _text[$"Progress{progress.Stage}"];
        if (progress.TotalUnits is > 0)
        {
            OperationProgress.IsIndeterminate = false;
            OperationProgress.Maximum = progress.TotalUnits.Value;
            OperationProgress.Value = progress.CompletedUnits;
        }
        else
        {
            OperationProgress.IsIndeterminate = true;
        }
    }

    private async Task ShowResultAsync(FlowWindowsOperationResult result)
    {
        ShowResultMessage(
            result.Succeeded ? _text["ResultSuccess"] : _text["ResultFailure"],
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        if (result.Summary is { } summary)
        {
            if (result.Operation == FlowWindowsOperationKind.Inspect && _sourcePath is not null)
            {
                OutputPathBox.Text = _operations.SuggestDocumentOutputPath(_sourcePath, summary.Title);
            }

            SummaryCard.Visibility = Visibility.Visible;
            SummaryTitle.Text = summary.Title;
            SummaryAuthors.Text = $"{_text["AuthorsLabel"]}: {(summary.Authors.Length == 0 ? "—" : string.Join(", ", summary.Authors))}";
            SummaryLanguage.Text = $"{_text["LanguageValueLabel"]}: {summary.Language ?? "—"}";
            SummaryVersion.Text = $"{_text["EpubVersionLabel"]}: {summary.EpubVersion ?? "—"}";
            SummaryResources.Text = $"{_text["ResourcesLabel"]}: {summary.ResourceCount}; {_text["SpineLabel"]}: {summary.SpineItemCount}; {_text["AssetsLabel"]}: {summary.AssetCount}";
            SummaryOutputs.Text = string.Join("\n", new[]
            {
                result.DocumentOutputPath,
                result.DiagnosticsOutputPath,
                result.HtmlBookOutputPath,
            }.Where(static value => value is not null).Select(value => $"{_text["OutputCreated"]}: {value}"));
            await ShowCoverAsync(summary);
        }

        DiagnosticsExpander.Visibility = Visibility.Visible;
        DiagnosticsExpander.Header = _text["DiagnosticsTitle"];
        DiagnosticsList.ItemsSource = result.Diagnostics.Length == 0
            ? [_text["NoDiagnostics"]]
            : result.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Severity} {diagnostic.Code}"
                    + (diagnostic.Location is null ? string.Empty : $" [{diagnostic.Location}]")
                    + $": {diagnostic.Message}"
                    + (diagnostic.Count == 1 ? string.Empty : $" (x{diagnostic.Count})"))
                .ToArray();
    }

    private async Task ShowCoverAsync(FlowWindowsBookSummary summary)
    {
        if (summary.CoverBytes.IsDefaultOrEmpty)
        {
            CoverImage.Source = null;
            NoCoverText.Text = _text["NoCover"];
            NoCoverText.Visibility = Visibility.Visible;
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
        AutomationProperties.SetName(CoverImage, $"{_text["CoverLabel"]}: {summary.Title}");
        NoCoverText.Visibility = Visibility.Collapsed;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = _text["ConfirmButton"],
            CloseButtonText = _text["BackButton"],
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetBusy(bool busy)
    {
        ChooseButton.IsEnabled = !busy;
        InspectButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        ValidateButton.IsEnabled = !busy;
        ImportOptions.IsEnabled = !busy;
        RunButton.IsEnabled = !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowInvalidSelection() =>
        ShowResultMessage(_text["InvalidSelection"], InfoBarSeverity.Warning);

    private void ShowResultMessage(string message, InfoBarSeverity severity, string? details = null)
    {
        ResultBar.Message = details is null ? message : $"{message} {details}";
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }

    private void Localize()
    {
        TitleText.Text = _text["OperationsTitle"];
        LeadText.Text = _text["OperationsLead"];
        DropText.Text = _text["DropFile"];
        ChooseButton.Content = _text["ChooseFile"];
        ReadOnlyNotice.Text = _text["SourceReadOnlyNotice"];
        InspectButton.Content = _text["InspectAction"];
        ImportButton.Content = _text["ImportAction"];
        ValidateButton.Content = _text["ValidateAction"];
        ImportOptions.Header = _text["OutputOptions"];
        OutputPathLabel.Text = _text["OutputPath"];
        DiagnosticsCheck.Content = _text["DiagnosticsReport"];
        HtmlBookCheck.Content = _text["HtmlBook"];
        HtmlLanguageLabel.Text = _text["HtmlLanguage"];
        AutomaticLanguageItem.Content = _text["LanguageAutomatic"];
        EnglishLanguageItem.Content = _text["LanguageEnglish"];
        PortugueseBrazilLanguageItem.Content = _text["LanguagePortugueseBrazil"];
        PortuguesePortugalLanguageItem.Content = _text["LanguagePortuguesePortugal"];
        CancelButton.Content = _text["CancelAction"];
        RunButton.Content = _text["RunAction"];
        AutomationProperties.SetName(DropArea, _text["DropFile"]);
        AutomationProperties.SetName(OutputPathBox, _text["OutputPath"]);
        AutomationProperties.SetName(HtmlLanguageSelector, _text["HtmlLanguage"]);
        AutomationProperties.SetName(RunButton, _text["RunAction"]);
    }

    private static bool IsSupportedPath(string path) =>
        Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".flow.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsOperationSupported(string path, FlowWindowsOperationKind operation) =>
        operation == FlowWindowsOperationKind.Validate
            ? IsSupportedPath(path)
            : Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase);
}
