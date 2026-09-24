using System.Collections.Immutable;
using System.Globalization;
using Flow.Rendering.Html;

namespace Flow.Cli;

internal sealed class CliDocumentAssistant(
    ICliAssistantView view,
    CliTextCatalog text,
    CliMenuCommandExecutor executor)
{
    private readonly ICliAssistantView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly CliTextCatalog _text = text ?? throw new ArgumentNullException(nameof(text));
    private readonly CliMenuCommandExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public Task RunAsync(string commandName, CancellationToken cancellationToken) => commandName switch
    {
        "inspect" => RunReadOnlyAsync(commandName, static path => new InspectCommand(path), cancellationToken),
        "validate" => RunReadOnlyAsync(commandName, static path => new ValidateCommand(path), cancellationToken),
        "hash" => RunReadOnlyAsync(commandName, static path => new HashCommand(path), cancellationToken),
        "render" => RunRenderAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(
            nameof(commandName),
            commandName,
            "No Flow document assistant is available."),
    };

    private async Task RunReadOnlyAsync(
        string commandName,
        Func<string, CliCommand> createCommand,
        CancellationToken cancellationToken)
    {
        var documentPath = await PromptForFlowDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (documentPath is null)
        {
            return;
        }

        await ConfirmAndExecuteAsync(
                createCommand(documentPath),
                new CliAssistantSummary(
                    _text.Get("DocumentAssistantReadOnlySummaryTitle"),
                    [
                        (_text.Get("DocumentAssistantSummaryDocument"), documentPath),
                        (_text.Get("DocumentAssistantSummaryOperation"),
                            _text.Get($"DocumentAssistantOperation_{commandName}")),
                    ],
                    [_text.Get("DocumentAssistantReadOnlyNotice")]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunRenderAsync(CancellationToken cancellationToken)
    {
        var documentPath = await PromptForFlowDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (documentPath is null)
        {
            return;
        }

        var renderType = await _view.SelectOneAsync(
                _text.Get("DocumentAssistantRenderTypePrompt"),
                [
                    new("standalone", _text.Get("DocumentAssistantRenderStandalone"),
                        _text.Get("DocumentAssistantRenderStandaloneDescription")),
                    new("html-book", _text.Get("DocumentAssistantRenderBook"),
                        _text.Get("DocumentAssistantRenderBookDescription")),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (renderType.Action != CliAssistantChoiceAction.Select || renderType.ChoiceId == "back")
        {
            return;
        }

        if (renderType.ChoiceId == "standalone")
        {
            await RunStandaloneRenderAsync(documentPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunHtmlBookRenderAsync(documentPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunStandaloneRenderAsync(string documentPath, CancellationToken cancellationToken)
    {
        var outputPath = await PromptForDestinationAsync(
                "DocumentAssistantHtmlOutputPrompt",
                documentPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (outputPath is null)
        {
            return;
        }

        var width = await PromptForDimensionAsync("DocumentAssistantWidthPrompt", cancellationToken)
            .ConfigureAwait(false);
        if (width is null)
        {
            return;
        }

        var height = await PromptForDimensionAsync("DocumentAssistantHeightPrompt", cancellationToken)
            .ConfigureAwait(false);
        if (height is null)
        {
            return;
        }

        await ConfirmAndExecuteAsync(
                new RenderHtmlCommand(documentPath, outputPath, width.Value, height.Value),
                new CliAssistantSummary(
                    _text.Get("DocumentAssistantRenderSummaryTitle"),
                    [
                        (_text.Get("DocumentAssistantSummaryDocument"), documentPath),
                        (_text.Get("DocumentAssistantSummaryRenderType"),
                            _text.Get("DocumentAssistantRenderStandalone")),
                        (_text.Get("DocumentAssistantSummaryDestination"), DescribeDestination(outputPath)),
                        (_text.Get("DocumentAssistantSummaryViewport"),
                            _text.Format(
                                "DocumentAssistantViewportValue",
                                FormatNumber(width.Value),
                                FormatNumber(height.Value))),
                    ],
                    ReplacementWarnings(outputPath)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunHtmlBookRenderAsync(string documentPath, CancellationToken cancellationToken)
    {
        var outputDirectory = await PromptForDestinationAsync(
                "DocumentAssistantBookOutputPrompt",
                documentPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (outputDirectory is null)
        {
            return;
        }

        var languageChoice = await _view.SelectOneAsync(
                _text.Get("DocumentAssistantUiLanguagePrompt"),
                [
                    new("auto", _text.Get("DocumentAssistantUiLanguageAuto")),
                    new("en", "en"),
                    new("pt-PT", "pt-PT"),
                    new("pt-BR", "pt-BR"),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (languageChoice.Action != CliAssistantChoiceAction.Select || languageChoice.ChoiceId == "back")
        {
            return;
        }

        var uiLanguage = languageChoice.ChoiceId switch
        {
            "auto" => HtmlBookUiLanguage.Automatic,
            "en" => HtmlBookUiLanguage.English,
            "pt-PT" => HtmlBookUiLanguage.PortuguesePortugal,
            "pt-BR" => HtmlBookUiLanguage.PortugueseBrazil,
            _ => throw new InvalidOperationException("The selected HTML book language is unknown."),
        };
        await ConfirmAndExecuteAsync(
                new RenderHtmlBookCommand(documentPath, outputDirectory, uiLanguage),
                new CliAssistantSummary(
                    _text.Get("DocumentAssistantRenderSummaryTitle"),
                    [
                        (_text.Get("DocumentAssistantSummaryDocument"), documentPath),
                        (_text.Get("DocumentAssistantSummaryRenderType"),
                            _text.Get("DocumentAssistantRenderBook")),
                        (_text.Get("DocumentAssistantSummaryDestination"), DescribeDestination(outputDirectory)),
                        (_text.Get("DocumentAssistantSummaryUiLanguage"), languageChoice.ChoiceId),
                    ],
                    ReplacementWarnings(outputDirectory)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> PromptForFlowDocumentAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _view.PromptTextAsync(
                    _text.Get("DocumentAssistantPathPrompt"),
                    _text.Get("AssistantTextCancellationHint"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCancelled || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            if (TryNormalizeFlowDocumentPath(result.Value, out var path, out var errorResourceKey))
            {
                return path;
            }

            await _view.ShowMessageAsync(
                    _text.Get("AssistantInvalidInputTitle"),
                    _text.Get(errorResourceKey),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> PromptForDestinationAsync(
        string promptResourceKey,
        string documentPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _view.PromptTextAsync(
                    _text.Get(promptResourceKey),
                    _text.Get("AssistantTextCancellationHint"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCancelled || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            try
            {
                var path = Path.GetFullPath(result.Value);
                if (PathsEqual(path, documentPath))
                {
                    await _view.ShowMessageAsync(
                            _text.Get("AssistantInvalidInputTitle"),
                            _text.Get("DocumentAssistantDestinationMatchesSource"),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                return path;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                await _view.ShowMessageAsync(
                        _text.Get("AssistantInvalidInputTitle"),
                        _text.Get("AssistantInvalidOutputPath"),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<double?> PromptForDimensionAsync(
        string promptResourceKey,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _view.PromptTextAsync(
                    _text.Get(promptResourceKey),
                    _text.Get("DocumentAssistantDimensionHint"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCancelled || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            if (double.TryParse(
                    result.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                && double.IsFinite(value)
                && value > 0)
            {
                return value;
            }

            await _view.ShowMessageAsync(
                    _text.Get("AssistantInvalidInputTitle"),
                    _text.Get("DocumentAssistantInvalidDimension"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> ConfirmAndExecuteAsync(
        CliCommand command,
        CliAssistantSummary summary,
        CancellationToken cancellationToken)
    {
        await _view.ShowSummaryAsync(summary, cancellationToken).ConfigureAwait(false);
        var confirmation = await _view.SelectOneAsync(
                _text.Get("AssistantConfirmationPrompt"),
                [
                    new("run", _text.Get("AssistantRun")),
                    new("back", _text.Get("AssistantCancelBeforeRun")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (confirmation.Action != CliAssistantChoiceAction.Select || confirmation.ChoiceId != "run")
        {
            return false;
        }

        var result = await CliAssistantExecution.ExecuteAsync(
                _view,
                command,
                _executor,
                cancellationToken)
            .ConfigureAwait(false);
        await _view.ShowExecutionResultAsync(result, cancellationToken).ConfigureAwait(false);
        await _view.WaitForReturnAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static bool TryNormalizeFlowDocumentPath(
        string candidate,
        out string path,
        out string errorResourceKey)
    {
        path = string.Empty;
        errorResourceKey = "DocumentAssistantInvalidPath";
        try
        {
            path = Path.GetFullPath(candidate);
            if (!path.EndsWith(".flow.json", StringComparison.OrdinalIgnoreCase))
            {
                errorResourceKey = "DocumentAssistantExtensionRequired";
                return false;
            }

            if (!File.Exists(path))
            {
                errorResourceKey = "DocumentAssistantNotFound";
                return false;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                errorResourceKey = "DocumentAssistantRegularFileRequired";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private ImmutableArray<string> ReplacementWarnings(string destination)
    {
        var warnings = ImmutableArray.CreateBuilder<string>();
        warnings.Add(_text.Get("DocumentAssistantRenderPolicyNotice"));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            warnings.Add(_text.Get("DocumentAssistantExistingDestinationWarning"));
        }

        return warnings.ToImmutable();
    }

    private string DescribeDestination(string path) =>
        File.Exists(path) || Directory.Exists(path)
            ? _text.Format("DocumentAssistantExistingDestination", path)
            : _text.Format("DocumentAssistantNewDestination", path);

    private static string FormatNumber(double value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);

    private static bool PathsEqual(string left, string right) => string.Equals(
        left,
        right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
