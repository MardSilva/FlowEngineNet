using System.Collections.Immutable;
using System.Globalization;
using Flow.Epub;

namespace Flow.Cli;

internal enum CliAdvancedInputKind
{
    ExistingEpub,
    ExistingFile,
    ExistingDirectory,
    Path,
    CandidateId,
    Sha256,
    RepetitionCount,
    ExecutionId,
}

internal sealed record CliAdvancedField(
    string Id,
    string? Option,
    string PromptResourceKey,
    string SummaryResourceKey,
    CliAdvancedInputKind Kind,
    bool Optional = false);

internal sealed record CliAdvancedWorkflow(
    string CommandName,
    ImmutableArray<CliAdvancedField> Fields,
    bool RequiresLegalDeclarations = false,
    bool SupportsUiLanguage = false,
    bool SupportsIncludeEnvironment = false,
    bool SupportsForce = false,
    bool SupportsResume = false,
    bool RequiresReinforcedCleanupConfirmation = false);

internal sealed class CliAdvancedAssistant(
    ICliAssistantView view,
    CliTextCatalog text,
    CliMenuCommandExecutor executor)
{
    private readonly ICliAssistantView _view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly CliTextCatalog _text = text ?? throw new ArgumentNullException(nameof(text));
    private readonly CliMenuCommandExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly CliCommandParser _parser = new();

    private static ImmutableArray<CliAdvancedWorkflow> Workflows { get; } =
    [
        new("epub-inventory",
        [
            Field("source", null, "AdvancedPromptSourceDirectory", "AdvancedSummarySourceDirectory", CliAdvancedInputKind.ExistingDirectory),
            Field("output", "--output", "AdvancedPromptCatalogOutput", "AdvancedSummaryCatalogOutput", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
        ], SupportsForce: true),
        new("epub-inventory-qualify",
        [
            Field("source", null, "AdvancedPromptSourceDirectory", "AdvancedSummarySourceDirectory", CliAdvancedInputKind.ExistingDirectory),
            Field("report", "--report", "AdvancedPromptQualificationOutput", "AdvancedSummaryReport", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
        ], RequiresLegalDeclarations: true, SupportsForce: true, SupportsResume: true),
        new("epub-inventory-matrix",
        [
            Field("qualification", null, "AdvancedPromptQualificationReport", "AdvancedSummaryQualificationReport", CliAdvancedInputKind.ExistingFile),
            Field("qualification-hash", "--qualification-sha256", "AdvancedPromptQualificationHash", "AdvancedSummaryExpectedHash", CliAdvancedInputKind.Sha256),
            Field("output", "--output", "AdvancedPromptMatrixOutput", "AdvancedSummaryMatrixOutput", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
        ], SupportsForce: true, SupportsResume: true),
        new("epub-inventory-review",
        [
            Field("source", null, "AdvancedPromptSourceDirectory", "AdvancedSummarySourceDirectory", CliAdvancedInputKind.ExistingDirectory),
            Field("qualification", "--qualification", "AdvancedPromptQualificationReport", "AdvancedSummaryQualificationReport", CliAdvancedInputKind.ExistingFile),
            Field("qualification-hash", "--qualification-sha256", "AdvancedPromptQualificationHash", "AdvancedSummaryExpectedHash", CliAdvancedInputKind.Sha256),
            Field("output", "--output", "AdvancedPromptReviewOutput", "AdvancedSummaryReviewOutput", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
        ], RequiresLegalDeclarations: true, SupportsUiLanguage: true, SupportsForce: true, SupportsResume: true),
        new("corpus",
        [
            Field("manifest", null, "AdvancedPromptCorpusManifest", "AdvancedSummaryCorpusManifest", CliAdvancedInputKind.ExistingFile),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
            Field("report", "--report", "AdvancedPromptCorpusReport", "AdvancedSummaryReport", CliAdvancedInputKind.Path),
            Field("external-root", "--external-root", "AdvancedPromptExternalRoot", "AdvancedSummaryExternalRoot", CliAdvancedInputKind.ExistingDirectory, Optional: true),
            Field("baseline", "--baseline", "AdvancedPromptBaseline", "AdvancedSummaryBaseline", CliAdvancedInputKind.ExistingFile, Optional: true),
        ], SupportsForce: true, SupportsResume: true),
        new("epub-qualify",
        [
            Field("source", null, "AdvancedPromptSourceEpub", "AdvancedSummarySourceEpub", CliAdvancedInputKind.ExistingEpub),
            Field("candidate", "--candidate-id", "AdvancedPromptCandidateId", "AdvancedSummaryCandidateId", CliAdvancedInputKind.CandidateId),
            Field("source-hash", "--sha256", "AdvancedPromptSourceHash", "AdvancedSummaryExpectedHash", CliAdvancedInputKind.Sha256),
            Field("report", "--report", "AdvancedPromptGateReport", "AdvancedSummaryReport", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
            Field("repetitions", "--repetitions", "AdvancedPromptRepetitions", "AdvancedSummaryRepetitions", CliAdvancedInputKind.RepetitionCount, Optional: true),
        ], RequiresLegalDeclarations: true, SupportsIncludeEnvironment: true, SupportsForce: true, SupportsResume: true),
        new("epub-review",
        [
            Field("source", null, "AdvancedPromptSourceEpub", "AdvancedSummarySourceEpub", CliAdvancedInputKind.ExistingEpub),
            Field("candidate", "--candidate-id", "AdvancedPromptCandidateId", "AdvancedSummaryCandidateId", CliAdvancedInputKind.CandidateId),
            Field("source-hash", "--sha256", "AdvancedPromptSourceHash", "AdvancedSummaryExpectedHash", CliAdvancedInputKind.Sha256),
            Field("output", "--output", "AdvancedPromptReviewOutput", "AdvancedSummaryReviewOutput", CliAdvancedInputKind.Path),
            Field("repository", "--repository-root", "AdvancedPromptRepositoryRoot", "AdvancedSummaryRepositoryRoot", CliAdvancedInputKind.ExistingDirectory),
        ], RequiresLegalDeclarations: true, SupportsUiLanguage: true, SupportsForce: true, SupportsResume: true),
        new("execution-status",
        [
            Field("destination", null, "AdvancedPromptExecutionDestination", "AdvancedSummaryExecutionDestination", CliAdvancedInputKind.Path),
            Field("json", "--json", "AdvancedPromptStatusOutput", "AdvancedSummaryStatusOutput", CliAdvancedInputKind.Path, Optional: true),
        ], SupportsForce: true),
        new("execution-clean",
        [
            Field("destination", null, "AdvancedPromptExecutionDestination", "AdvancedSummaryExecutionDestination", CliAdvancedInputKind.Path),
            Field("execution-id", "--execution-id", "AdvancedPromptExecutionId", "AdvancedSummaryExecutionId", CliAdvancedInputKind.ExecutionId),
        ], RequiresReinforcedCleanupConfirmation: true),
    ];

    public async Task RunAsync(string commandName, CancellationToken cancellationToken)
    {
        var workflow = Workflows.SingleOrDefault(item => item.CommandName == commandName)
            ?? throw new ArgumentOutOfRangeException(
                nameof(commandName),
                commandName,
                "No advanced assistant is available.");

        await _view.ShowMessageAsync(
                _text.Get($"AdvancedTitle_{commandName}"),
                _text.Format(
                    "AdvancedIntroduction",
                    _text.Get($"AdvancedPurpose_{commandName}"),
                    _text.Get($"AdvancedCost_{commandName}")),
                cancellationToken)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in workflow.Fields)
        {
            if (field.Optional)
            {
                var includeField = await ConfirmOptionalFieldAsync(field, cancellationToken).ConfigureAwait(false);
                if (includeField is null)
                {
                    return;
                }

                if (!includeField.Value)
                {
                    continue;
                }
            }

            var value = await PromptFieldAsync(field, cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return;
            }

            values.Add(field.Id, value);
        }

        if (workflow.RequiresLegalDeclarations
            && !await CollectLegalDeclarationsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var uiLanguage = workflow.SupportsUiLanguage
            ? await SelectUiLanguageAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (workflow.SupportsUiLanguage && uiLanguage is null)
        {
            return;
        }

        bool? includeEnvironment = false;
        if (workflow.SupportsIncludeEnvironment)
        {
            includeEnvironment = await PromptBooleanOptionAsync(
                    "AdvancedIncludeEnvironmentPrompt",
                    cancellationToken)
                .ConfigureAwait(false);
            if (includeEnvironment is null)
            {
                return;
            }
        }

        var force = workflow.SupportsForce
            ? await PromptBooleanOptionAsync("AdvancedForcePrompt", cancellationToken).ConfigureAwait(false)
            : false;
        if (workflow.SupportsForce && force is null)
        {
            return;
        }

        var resume = workflow.SupportsResume
            ? await PromptBooleanOptionAsync("AdvancedResumePrompt", cancellationToken).ConfigureAwait(false)
            : false;
        if (workflow.SupportsResume && resume is null)
        {
            return;
        }

        var command = await ParseCommandAsync(
                workflow,
                values,
                uiLanguage,
                includeEnvironment is true,
                force is true,
                resume is true,
                cancellationToken)
            .ConfigureAwait(false);
        if (command is null)
        {
            return;
        }

        var rows = workflow.Fields
            .Where(field => values.ContainsKey(field.Id))
            .Select(field => (_text.Get(field.SummaryResourceKey), values[field.Id]))
            .ToImmutableArray()
            .ToBuilder();
        if (uiLanguage is not null)
        {
            rows.Add((_text.Get("AdvancedSummaryUiLanguage"), uiLanguage));
        }

        if (workflow.RequiresLegalDeclarations)
        {
            rows.Add((_text.Get("AdvancedSummaryLegalUse"), _text.Get("AdvancedDeclaredExplicitly")));
            rows.Add((_text.Get("AdvancedSummaryDrmFree"), _text.Get("AdvancedDeclaredExplicitly")));
        }

        if (workflow.SupportsIncludeEnvironment)
        {
            AddEnabledOption(rows, "AdvancedSummaryIncludeEnvironment", includeEnvironment is true);
        }

        if (workflow.SupportsForce)
        {
            AddEnabledOption(rows, "AdvancedSummaryForce", force is true);
        }

        if (workflow.SupportsResume)
        {
            AddEnabledOption(rows, "AdvancedSummaryResume", resume is true);
        }

        var warnings = ImmutableArray.CreateBuilder<string>();
        warnings.Add(_text.Get($"AdvancedWarning_{commandName}"));
        if (force is true)
        {
            warnings.Add(_text.Get("AdvancedForceWarning"));
        }

        var summary = new CliAssistantSummary(
            _text.Get("AdvancedSummaryTitle"),
            rows.ToImmutable(),
            warnings.ToImmutable());
        await _view.ShowSummaryAsync(summary, cancellationToken).ConfigureAwait(false);

        if (workflow.RequiresReinforcedCleanupConfirmation
            && !await ConfirmCleanupIdAsync(values["execution-id"], cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var confirmation = await _view.SelectOneAsync(
                _text.Get("AssistantConfirmationPrompt"),
                [
                    new("back", _text.Get("AssistantCancelBeforeRun")),
                    new("run", _text.Get("AssistantRun")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (confirmation.Action != CliAssistantChoiceAction.Select || confirmation.ChoiceId != "run")
        {
            return;
        }

        var result = await CliAssistantExecution.ExecuteAsync(
                _view,
                command,
                _executor,
                cancellationToken)
            .ConfigureAwait(false);
        await _view.ShowExecutionResultAsync(result, cancellationToken).ConfigureAwait(false);
        await _view.WaitForReturnAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CliAdvancedField Field(
        string id,
        string? option,
        string promptResourceKey,
        string summaryResourceKey,
        CliAdvancedInputKind kind,
        bool Optional = false) => new(id, option, promptResourceKey, summaryResourceKey, kind, Optional);

    private async Task<bool?> ConfirmOptionalFieldAsync(
        CliAdvancedField field,
        CancellationToken cancellationToken)
    {
        var choice = await _view.SelectOneAsync(
                _text.Format("AdvancedOptionalFieldPrompt", _text.Get(field.SummaryResourceKey)),
                [
                    new("no", _text.Get("AdvancedNo")),
                    new("yes", _text.Get("AdvancedYes")),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (choice.Action != CliAssistantChoiceAction.Select || choice.ChoiceId == "back")
        {
            return null;
        }

        return choice.ChoiceId == "yes";
    }

    private async Task<string?> PromptFieldAsync(
        CliAdvancedField field,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await _view.PromptTextAsync(
                    _text.Get(field.PromptResourceKey),
                    _text.Get("AssistantTextCancellationHint"),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCancelled || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            if (TryNormalizeValue(result.Value, field.Kind, out var value, out var errorKey))
            {
                return value;
            }

            await _view.ShowMessageAsync(
                    _text.Get("AssistantInvalidInputTitle"),
                    _text.Get(errorKey),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private bool TryNormalizeValue(
        string candidate,
        CliAdvancedInputKind kind,
        out string value,
        out string errorResourceKey)
    {
        value = candidate.Trim();
        errorResourceKey = "AdvancedInvalidValue";
        if (kind == CliAdvancedInputKind.CandidateId)
        {
            return EpubCorpusPublicationId.TryParse(value, out _);
        }

        if (kind == CliAdvancedInputKind.Sha256)
        {
            value = value.ToUpperInvariant();
            return EpubCorpusSha256.TryParse(value, out _);
        }

        if (kind == CliAdvancedInputKind.RepetitionCount)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 2;
        }

        if (kind == CliAdvancedInputKind.ExecutionId)
        {
            value = value.ToLowerInvariant();
            return Guid.TryParseExact(value, "N", out _);
        }

        try
        {
            value = Path.GetFullPath(value);
            if (kind == CliAdvancedInputKind.ExistingEpub)
            {
                if (!CliEpubAssistant.TryNormalizeEpubPath(value, out value, out errorResourceKey))
                {
                    return false;
                }

                return true;
            }

            if (kind == CliAdvancedInputKind.ExistingFile && !IsRegularExistingFile(value))
            {
                errorResourceKey = "AdvancedExistingFileRequired";
                return false;
            }

            if (kind == CliAdvancedInputKind.ExistingDirectory && !IsRegularExistingDirectory(value))
            {
                errorResourceKey = "AdvancedExistingDirectoryRequired";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            errorResourceKey = "AdvancedInvalidPath";
            return false;
        }
    }

    private async Task<bool> CollectLegalDeclarationsAsync(CancellationToken cancellationToken)
    {
        await _view.ShowMessageAsync(
                _text.Get("AdvancedLegalTitle"),
                _text.Get("AdvancedLegalNotice"),
                cancellationToken)
            .ConfigureAwait(false);
        return await CollectDeclarationAsync(
                "AdvancedLegalUsePrompt",
                "AdvancedDeclareLegalUse",
                cancellationToken)
            .ConfigureAwait(false)
            && await CollectDeclarationAsync(
                "AdvancedDrmFreePrompt",
                "AdvancedDeclareDrmFree",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> CollectDeclarationAsync(
        string promptResourceKey,
        string declarationResourceKey,
        CancellationToken cancellationToken)
    {
        var choice = await _view.SelectOneAsync(
                _text.Get(promptResourceKey),
                [
                    new("cancel", _text.Get("AdvancedDoNotDeclare")),
                    new("declare", _text.Get(declarationResourceKey)),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        return choice.Action == CliAssistantChoiceAction.Select && choice.ChoiceId == "declare";
    }

    private async Task<string?> SelectUiLanguageAsync(CancellationToken cancellationToken)
    {
        var choice = await _view.SelectOneAsync(
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
        return choice.Action == CliAssistantChoiceAction.Select && choice.ChoiceId != "back"
            ? choice.ChoiceId
            : null;
    }

    private async Task<bool?> PromptBooleanOptionAsync(
        string promptResourceKey,
        CancellationToken cancellationToken)
    {
        var choice = await _view.SelectOneAsync(
                _text.Get(promptResourceKey),
                [
                    new("no", _text.Get("AdvancedNo")),
                    new("yes", _text.Get("AdvancedYes")),
                    new("back", _text.Get("MenuBack")),
                ],
                cancellationToken)
            .ConfigureAwait(false);
        if (choice.Action != CliAssistantChoiceAction.Select || choice.ChoiceId == "back")
        {
            return null;
        }

        return choice.ChoiceId == "yes";
    }

    private async Task<CliCommand?> ParseCommandAsync(
        CliAdvancedWorkflow workflow,
        IReadOnlyDictionary<string, string> values,
        string? uiLanguage,
        bool includeEnvironment,
        bool force,
        bool resume,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { workflow.CommandName };
        foreach (var field in workflow.Fields)
        {
            if (!values.TryGetValue(field.Id, out var value))
            {
                continue;
            }

            if (field.Option is not null)
            {
                arguments.Add(field.Option);
            }

            arguments.Add(value);
        }

        if (workflow.RequiresLegalDeclarations)
        {
            arguments.Add("--legal-use");
            arguments.Add("--drm-free");
        }

        AddOption(arguments, "--ui-language", uiLanguage);
        AddFlag(arguments, "--include-environment", includeEnvironment);
        AddFlag(arguments, "--force", force);
        AddFlag(arguments, "--resume", resume);

        var result = _parser.Parse(arguments);
        if (result.Command is not null)
        {
            return result.Command;
        }

        var message = result.Diagnostic?.Format(_text) ?? result.Error ?? _text.Get("AdvancedInvalidValue");
        await _view.ShowMessageAsync(
                _text.Get("AssistantInvalidInputTitle"),
                message,
                cancellationToken)
            .ConfigureAwait(false);
        return null;
    }

    private async Task<bool> ConfirmCleanupIdAsync(
        string executionId,
        CancellationToken cancellationToken)
    {
        await _view.ShowMessageAsync(
                _text.Get("AdvancedCleanupConfirmationTitle"),
                _text.Get("AdvancedCleanupConfirmationNotice"),
                cancellationToken)
            .ConfigureAwait(false);
        var entered = await _view.PromptTextAsync(
                _text.Get("AdvancedCleanupRepeatIdPrompt"),
                _text.Get("AssistantTextCancellationHint"),
                cancellationToken)
            .ConfigureAwait(false);
        if (entered.IsCancelled || !string.Equals(
                entered.Value?.Trim(),
                executionId,
                StringComparison.OrdinalIgnoreCase))
        {
            await _view.ShowMessageAsync(
                    _text.Get("AdvancedCleanupConfirmationTitle"),
                    _text.Get("AdvancedCleanupIdMismatch"),
                    cancellationToken)
                .ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private void AddEnabledOption(
        ImmutableArray<(string Label, string Value)>.Builder rows,
        string resourceKey,
        bool enabled)
    {
        rows.Add((_text.Get(resourceKey), _text.Get(enabled ? "AdvancedYes" : "AdvancedNo")));
    }

    private static bool IsRegularExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private static bool IsRegularExistingDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static void AddOption(List<string> arguments, string option, string? value)
    {
        if (value is null)
        {
            return;
        }

        arguments.Add(option);
        arguments.Add(value);
    }

    private static void AddFlag(List<string> arguments, string option, bool enabled)
    {
        if (enabled)
        {
            arguments.Add(option);
        }
    }
}
