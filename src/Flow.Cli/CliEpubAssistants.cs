using System.Collections.Immutable;

namespace Flow.Cli;

internal enum CliMenuDetailsAction
{
    Back,
    RunAssistant,
    Cancel,
}

internal enum CliAssistantChoiceAction
{
    Select,
    Back,
    Cancel,
}

internal sealed record CliAssistantChoice(
    string Id,
    string Label,
    string? Description = null);

internal sealed record CliAssistantChoiceResult(
    CliAssistantChoiceAction Action,
    string? ChoiceId = null);

internal sealed record CliAssistantTextResult(bool IsCancelled, string? Value)
{
    public static CliAssistantTextResult Cancelled { get; } = new(true, null);

    public static CliAssistantTextResult Entered(string value) => new(false, value);
}

internal sealed record CliAssistantMultiChoiceResult(
    bool IsCancelled,
    ImmutableArray<string> ChoiceIds);

internal sealed record CliAssistantSummary(
    string Title,
    ImmutableArray<(string Label, string Value)> Rows,
    ImmutableArray<string> Warnings);

internal sealed record CliMenuExecutionResult(
    int ExitCode,
    ImmutableArray<string> WrittenFiles);

internal delegate Task<CliMenuExecutionResult> CliMenuCommandExecutor(
    CliCommand command,
    CancellationToken cancellationToken);

internal interface ICliAssistantView
{
    public Task<CliAssistantTextResult> PromptTextAsync(
        string title,
        string cancellationHint,
        CancellationToken cancellationToken);

    public Task<CliAssistantChoiceResult> SelectOneAsync(
        string title,
        ImmutableArray<CliAssistantChoice> choices,
        CancellationToken cancellationToken);

    public Task<CliAssistantMultiChoiceResult> SelectManyAsync(
        string title,
        ImmutableArray<CliAssistantChoice> choices,
        CancellationToken cancellationToken);

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken);

    public Task ShowSummaryAsync(CliAssistantSummary summary, CancellationToken cancellationToken);

    public Task ShowExecutionResultAsync(
        CliMenuExecutionResult result,
        CancellationToken cancellationToken);

    public Task WaitForReturnAsync(CancellationToken cancellationToken);
}

internal sealed class CliEpubAssistant(
    ICliAssistantView view,
    CliTextCatalog text,
    CliMenuCommandExecutor executor)
{
    private readonly ICliAssistantView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly CliTextCatalog _text = text ?? throw new ArgumentNullException(nameof(text));
    private readonly CliMenuCommandExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public Task RunAsync(string commandName, CancellationToken cancellationToken) => commandName switch
    {
        "import" => RunImportAsync(cancellationToken),
        "epub-inspect" => RunInspectionAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "No EPUB assistant is available."),
    };

    private async Task RunImportAsync(CancellationToken cancellationToken)
    {
        var sourcePath = await PromptForEpubAsync(cancellationToken).ConfigureAwait(false);
        if (sourcePath is null)
        {
            return;
        }

        var destinationChoice = await _view.SelectOneAsync(
                _text.Get("AssistantImportDestinationPrompt"),
                [
                    new("automatic", _text.Get("AssistantImportDestinationAutomatic"),
                        _text.Get("AssistantImportDestinationAutomaticDescription")),
                    new("custom", _text.Get("AssistantImportDestinationCustom"),
                        _text.Get("AssistantImportDestinationCustomDescription")),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (destinationChoice.Action != CliAssistantChoiceAction.Select
            || destinationChoice.ChoiceId == "back")
        {
            return;
        }

        string? outputPath = null;
        if (destinationChoice.ChoiceId == "custom")
        {
            outputPath = await PromptForOutputPathAsync(
                    "AssistantImportOutputPrompt",
                    sourcePath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (outputPath is null)
            {
                return;
            }
        }

        var reports = ImportReportChoices();
        var reportSelection = await _view.SelectManyAsync(
                _text.Get("AssistantImportReportsPrompt"),
                reports,
                cancellationToken)
            .ConfigureAwait(false);
        if (reportSelection.IsCancelled)
        {
            return;
        }

        var reportPaths = reportSelection.ChoiceIds.ToDictionary(
            static id => id,
            id => SuggestedReportPath(sourcePath, id),
            StringComparer.Ordinal);
        var command = new ImportEpubCommand(
            sourcePath,
            outputPath,
            GetReportPath(reportPaths, "diagnostics"),
            GetReportPath(reportPaths, "fidelity"),
            GetReportPath(reportPaths, "metadata"),
            GetReportPath(reportPaths, "processing"),
            GetReportPath(reportPaths, "source-map"));

        var outputDescription = outputPath ?? _text.Get("AssistantImportAutomaticOutputSummary");
        var rows = ImmutableArray.CreateBuilder<(string Label, string Value)>();
        rows.Add((_text.Get("AssistantSummarySource"), sourcePath));
        rows.Add((_text.Get("AssistantSummaryFlowOutput"), DescribeOutput(outputDescription, outputPath)));
        rows.Add((_text.Get("AssistantSummaryReports"), reportPaths.Count == 0
            ? _text.Get("ValueNone")
            : string.Join(Environment.NewLine, reportPaths.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{_text.Get(ReportLabelKey(item.Key))}: {DescribeOutput(item.Value, item.Value)}"))));

        if (!await ConfirmAndExecuteAsync(
                command,
                new CliAssistantSummary(
                    _text.Get("AssistantImportSummaryTitle"),
                    rows.ToImmutable(),
                    outputPath is null
                        ?
                        [
                            _text.Get("AssistantAtomicWriteNotice"),
                            _text.Get("AssistantAutomaticOutputReplacementNotice"),
                        ]
                        : [_text.Get("AssistantAtomicWriteNotice")]),
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }
    }

    private async Task RunInspectionAsync(CancellationToken cancellationToken)
    {
        var sourcePath = await PromptForEpubAsync(cancellationToken).ConfigureAwait(false);
        if (sourcePath is null)
        {
            return;
        }

        var reportChoice = await _view.SelectOneAsync(
                _text.Get("AssistantInspectReportPrompt"),
                [
                    new("none", _text.Get("AssistantInspectReportNone")),
                    new("json", _text.Get("AssistantInspectReportJson"),
                        _text.Get("AssistantInspectReportJsonDescription")),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (reportChoice.Action != CliAssistantChoiceAction.Select || reportChoice.ChoiceId == "back")
        {
            return;
        }

        var reportPath = reportChoice.ChoiceId == "json"
            ? SuggestedReportPath(sourcePath, "inspection")
            : null;
        var rows = ImmutableArray.CreateBuilder<(string Label, string Value)>();
        rows.Add((_text.Get("AssistantSummarySource"), sourcePath));
        rows.Add((_text.Get("AssistantSummaryInspectionReport"), reportPath is null
            ? _text.Get("ValueNone")
            : DescribeOutput(reportPath, reportPath)));

        await ConfirmAndExecuteAsync(
                new InspectEpubCommand(sourcePath, reportPath),
                new CliAssistantSummary(
                    _text.Get("AssistantInspectSummaryTitle"),
                    rows.ToImmutable(),
                    reportPath is null ? [] : [_text.Get("AssistantAtomicWriteNotice")]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> PromptForEpubAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _view.PromptTextAsync(
                    _text.Get("AssistantEpubPathPrompt"),
                    _text.Get("AssistantTextCancellationHint"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCancelled || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            if (TryNormalizeEpubPath(result.Value, out var path, out var errorKey))
            {
                return path;
            }

            await _view.ShowMessageAsync(
                    _text.Get("AssistantInvalidInputTitle"),
                    _text.Get(errorKey),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> PromptForOutputPathAsync(
        string promptResourceKey,
        string sourcePath,
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
                if (PathsEqual(path, sourcePath))
                {
                    await _view.ShowMessageAsync(
                            _text.Get("AssistantInvalidInputTitle"),
                            _text.Get("AssistantOutputMatchesSource"),
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

        var result = await _executor(command, cancellationToken).ConfigureAwait(false);
        await _view.ShowExecutionResultAsync(result, cancellationToken).ConfigureAwait(false);
        await _view.WaitForReturnAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static bool TryNormalizeEpubPath(
        string candidate,
        out string path,
        out string errorResourceKey)
    {
        path = string.Empty;
        errorResourceKey = "AssistantInvalidEpubPath";
        try
        {
            path = Path.GetFullPath(candidate);
            if (!string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase))
            {
                errorResourceKey = "AssistantEpubExtensionRequired";
                return false;
            }

            if (!File.Exists(path))
            {
                errorResourceKey = "AssistantEpubNotFound";
                return false;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                errorResourceKey = "AssistantEpubRegularFileRequired";
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

    private ImmutableArray<CliAssistantChoice> ImportReportChoices() =>
    [
        new("diagnostics", _text.Get("AssistantReportDiagnostics"),
            _text.Get("AssistantReportDiagnosticsDescription")),
        new("fidelity", _text.Get("AssistantReportFidelity"),
            _text.Get("AssistantReportFidelityDescription")),
        new("metadata", _text.Get("AssistantReportMetadata"),
            _text.Get("AssistantReportMetadataDescription")),
        new("processing", _text.Get("AssistantReportProcessing"),
            _text.Get("AssistantReportProcessingDescription")),
        new("source-map", _text.Get("AssistantReportSourceMap"),
            _text.Get("AssistantReportSourceMapDescription")),
    ];

    private string DescribeOutput(string description, string? path) =>
        path is not null && File.Exists(path)
            ? _text.Format("AssistantExistingOutput", description)
            : description;

    private static string? GetReportPath(IReadOnlyDictionary<string, string> paths, string id) =>
        paths.TryGetValue(id, out var path) ? path : null;

    private static string SuggestedReportPath(string sourcePath, string reportId)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var suffix = reportId switch
        {
            "diagnostics" => "diagnostics",
            "fidelity" => "fidelity",
            "metadata" => "metadata",
            "processing" => "processing",
            "source-map" => "source-map",
            "inspection" => "inspection",
            _ => throw new ArgumentOutOfRangeException(nameof(reportId), reportId, "Unknown report."),
        };
        return Path.Combine(directory, $"{stem}.{suffix}.json");
    }

    private static string ReportLabelKey(string reportId) => reportId switch
    {
        "diagnostics" => "AssistantReportDiagnostics",
        "fidelity" => "AssistantReportFidelity",
        "metadata" => "AssistantReportMetadata",
        "processing" => "AssistantReportProcessing",
        "source-map" => "AssistantReportSourceMap",
        _ => throw new ArgumentOutOfRangeException(nameof(reportId), reportId, "Unknown report."),
    };

    private static bool PathsEqual(string left, string right) => string.Equals(
        left,
        right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
