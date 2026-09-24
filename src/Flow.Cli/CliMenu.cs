using System.Collections.Immutable;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Flow.Cli;

internal static class CliMenuDiagnosticCodes
{
    public const string RequiresInteractiveTerminal = "FLOWCLI_MENU_REQUIRES_INTERACTIVE";
    public const string PresentationFailed = "FLOWCLI_PRESENTATION_FAILED";
}

internal sealed record CliMenuGroup(
    CliCommandGroup? Group,
    string Title,
    ImmutableArray<CliCommandDescriptor> Commands);

internal sealed record CliMenuWelcome(
    string Title,
    string Description,
    string ReadOnlyNotice,
    string ExperimentalWarning)
{
    public static CliMenuWelcome Create(CliTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new CliMenuWelcome(
            text.Format("HelpTitle", CliProductInfo.Version),
            text.Get("MenuWelcomeDescription"),
            text.Get("MenuWelcomeReadOnly"),
            text.Get("HelpWarning"));
    }
}

internal sealed record CliMenuModel(ImmutableArray<CliMenuGroup> Groups)
{
    public static CliMenuModel Create(CliTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var commonGroups = Enum.GetValues<CliCommandGroup>()
            .Where(group => group is not CliCommandGroup.CorpusQuality and not CliCommandGroup.Maintenance)
                .Select(group => new CliMenuGroup(
                    group,
                    text.Get(CliCommandGroupResources.GetTitleKey(group)),
                    [.. CliCommandCatalog.All.Where(command =>
                        command.Group == group && command.AvailableInInteractiveMenu)]))
                .Where(group => !group.Commands.IsEmpty);
        var advancedCommands = CliCommandCatalog.All
            .Where(command => command.AvailableInInteractiveMenu
                && command.Group is CliCommandGroup.CorpusQuality or CliCommandGroup.Maintenance)
            .ToImmutableArray();

        return new CliMenuModel(
        [
            .. commonGroups,
            new CliMenuGroup(null, text.Get("MenuAdvancedTitle"), advancedCommands),
        ]);
    }
}

internal sealed record CliMenuDetails(
    string CommandName,
    string Title,
    string Description,
    string Usage,
    ImmutableArray<string> RequiredInputs,
    string FileEffects,
    ImmutableArray<string> SecurityNotes)
{
    public static CliMenuDetails Create(CliCommandDescriptor command, CliTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(text);

        var inputs = command.Arguments
            .Where(argument => argument.Requirement != CliRequirement.Optional)
            .Select(argument => FormatInput(
                argument.Syntax,
                argument.Requirement,
                argument.DescriptionResourceKey,
                text))
            .Concat(command.Options
                .Where(option => option.Requirement != CliRequirement.Optional)
                .Select(option => FormatInput(
                    option.Syntax,
                    option.Requirement,
                    option.DescriptionResourceKey,
                    text)))
            .ToImmutableArray();

        return new CliMenuDetails(
            command.Name,
            text.Get(command.TitleResourceKey),
            text.Get(command.DescriptionResourceKey),
            command.Usage,
            inputs.IsEmpty ? [text.Get("MenuNoRequiredInputs")] : inputs,
            text.Get(command.FileEffectsResourceKey),
            command.SecurityResourceKeys.IsEmpty
                ? [text.Get("MenuNoSecurityNotes")]
                : [.. command.SecurityResourceKeys.Select(text.Get)]);
    }

    private static string FormatInput(
        string syntax,
        CliRequirement requirement,
        string descriptionResourceKey,
        CliTextCatalog text) =>
        text.Format(
            "MenuDetailInputEntry",
            syntax,
            text.Get(CliRequirementResources.GetKey(requirement)),
            text.Get(descriptionResourceKey));
}

internal enum CliMenuMainAction
{
    OpenGroup,
    Exit,
    Cancel,
}

internal sealed record CliMenuMainSelection(CliMenuMainAction Action, CliMenuGroup? Group = null);

internal enum CliMenuCommandAction
{
    ShowDetails,
    Back,
    Cancel,
}

internal sealed record CliMenuCommandSelection(
    CliMenuCommandAction Action,
    CliCommandDescriptor? Command = null);

internal interface ICliMenuView
{
    public Task ShowWelcomeAsync(
        CliMenuWelcome welcome,
        CancellationToken cancellationToken);

    public Task<CliMenuMainSelection> SelectGroupAsync(
        CliMenuModel model,
        CancellationToken cancellationToken);

    public Task<CliMenuCommandSelection> SelectCommandAsync(
        CliMenuGroup group,
        CancellationToken cancellationToken);

    public Task<CliMenuDetailsAction> ShowDetailsAsync(
        CliMenuDetails details,
        bool assistantAvailable,
        CancellationToken cancellationToken);
}

internal interface ICliMenu
{
    public Task<int> RunAsync(
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CliPresentationProfile presentation,
        CliMenuCommandExecutor executor,
        CancellationToken cancellationToken);
}

internal sealed class CliMenuController
{
    private readonly ICliMenuView _view;
    private readonly ICliAssistantView _assistantView;

    public CliMenuController(ICliMenuView view)
        : this(view, view as ICliAssistantView ?? new UnavailableAssistantView())
    {
    }

    public CliMenuController(ICliMenuView view, ICliAssistantView assistantView)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _assistantView = assistantView ?? throw new ArgumentNullException(nameof(assistantView));
    }

    public Task<int> RunAsync(
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken) =>
        RunAsync(
            output,
            text,
            static (_, _, _) => throw new InvalidOperationException("No command executor was configured."),
            cancellationToken);

    internal static bool SupportsAssistant(string commandName) =>
        commandName is "import" or "epub-inspect" or "inspect" or "validate" or "hash" or "render"
            or "epub-inventory" or "epub-inventory-qualify" or "epub-inventory-matrix"
            or "epub-inventory-review" or "corpus" or "epub-qualify" or "epub-review"
            or "execution-status" or "execution-clean";

    public async Task<int> RunAsync(
        TextWriter output,
        CliTextCatalog text,
        CliMenuCommandExecutor executor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(executor);

        var model = CliMenuModel.Create(text);
        await _view.ShowWelcomeAsync(CliMenuWelcome.Create(text), cancellationToken).ConfigureAwait(false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mainSelection = await _view.SelectGroupAsync(model, cancellationToken).ConfigureAwait(false);
            if (mainSelection.Action == CliMenuMainAction.Cancel)
            {
                await output.WriteLineAsync(text.Get("MenuCancelled")).ConfigureAwait(false);
                return 0;
            }

            if (mainSelection.Action == CliMenuMainAction.Exit)
            {
                await output.WriteLineAsync(text.Get("MenuExited")).ConfigureAwait(false);
                return 0;
            }

            var group = mainSelection.Group
                ?? throw new InvalidOperationException("A menu group selection has no group.");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var commandSelection = await _view.SelectCommandAsync(group, cancellationToken)
                    .ConfigureAwait(false);
                if (commandSelection.Action == CliMenuCommandAction.Cancel)
                {
                    await output.WriteLineAsync(text.Get("MenuCancelled")).ConfigureAwait(false);
                    return 0;
                }

                if (commandSelection.Action == CliMenuCommandAction.Back)
                {
                    break;
                }

                var command = commandSelection.Command
                    ?? throw new InvalidOperationException("A command menu selection has no command.");
                var detailsAction = await _view.ShowDetailsAsync(
                        CliMenuDetails.Create(command, text),
                        SupportsAssistant(command.Name),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (detailsAction == CliMenuDetailsAction.Cancel)
                {
                    await output.WriteLineAsync(text.Get("MenuCancelled")).ConfigureAwait(false);
                    return 0;
                }

                if (detailsAction == CliMenuDetailsAction.RunAssistant)
                {
                    if (command.Name is "import" or "epub-inspect")
                    {
                        await new CliEpubAssistant(_assistantView, text, executor)
                            .RunAsync(command.Name, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (command.Name is "inspect" or "validate" or "hash" or "render")
                    {
                        await new CliDocumentAssistant(_assistantView, text, executor)
                            .RunAsync(command.Name, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await new CliAdvancedAssistant(_assistantView, text, executor)
                            .RunAsync(command.Name, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private sealed class UnavailableAssistantView : ICliAssistantView
    {
        private static InvalidOperationException Error() =>
            new("No EPUB assistant view was configured.");

        public Task<CliAssistantTextResult> PromptTextAsync(
            string title,
            string cancellationHint,
            CancellationToken cancellationToken) => throw Error();

        public Task<CliAssistantChoiceResult> SelectOneAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) => throw Error();

        public Task<CliAssistantMultiChoiceResult> SelectManyAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) => throw Error();

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken) =>
            throw Error();

        public Task ShowSummaryAsync(CliAssistantSummary summary, CancellationToken cancellationToken) =>
            throw Error();

        public Task ShowExecutionResultAsync(
            CliMenuExecutionResult result,
            CancellationToken cancellationToken) => throw Error();

        public Task WaitForReturnAsync(CancellationToken cancellationToken) => throw Error();
    }
}

internal sealed class SpectreCliMenu(IFlowTerminal? terminal = null) : ICliMenu
{
    private readonly IFlowTerminal _terminal = terminal ?? new SystemFlowTerminal();

    public Task<int> RunAsync(
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CliPresentationProfile presentation,
        CliMenuCommandExecutor executor,
        CancellationToken cancellationToken)
    {
        var view = new SpectreCliMenuView(output, text, presentation, _terminal);
        return new CliMenuController(view, view).RunAsync(output, text, executor, cancellationToken);
    }
}

internal sealed class SpectreCliMenuView : ICliMenuView, ICliAssistantView, ICliProgressAssistantView
{
    private const int PageSize = 8;
    private readonly IAnsiConsole _console;
    private readonly CliTextCatalog _text;
    private readonly bool _useColor;
    private bool _mainMenuShown;
    private bool _preserveNextPrompt;

    public SpectreCliMenuView(
        TextWriter output,
        CliTextCatalog text,
        CliPresentationProfile presentation,
        IFlowTerminal? terminal = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        _text = text ?? throw new ArgumentNullException(nameof(text));
        ArgumentNullException.ThrowIfNull(presentation);
        _useColor = presentation.UseColor;
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = presentation.UseColor ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = terminal is null
                ? new CliAnsiConsoleOutput(output, presentation.Width)
                : new CliAnsiConsoleOutput(output, () => SafeWidth(terminal, presentation.Width)),
        });
    }

    private static int SafeWidth(IFlowTerminal terminal, int fallback)
    {
        try
        {
            return Math.Clamp(terminal.GetWidth(), 20, 500);
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or NotSupportedException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException)
        {
            return fallback;
        }
    }

    public Task ShowWelcomeAsync(
        CliMenuWelcome welcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(welcome);
        cancellationToken.ThrowIfCancellationRequested();

        var content = new Rows(
            new Text(welcome.Description),
            new Text(string.Empty),
            new Text(welcome.ReadOnlyNotice),
            new Text(string.Empty),
            new Markup(_useColor
                ? $"[yellow]{CliMarkup.EscapeExternal(welcome.ExperimentalWarning)}[/]"
                : CliMarkup.EscapeExternal(welcome.ExperimentalWarning)));
        var panel = new Panel(content)
        {
            Border = BoxBorder.Rounded,
            Expand = true,
            Header = new PanelHeader(CliMarkup.EscapeExternal(welcome.Title)),
            Padding = new Padding(1, 0, 1, 0),
        };
        if (_useColor)
        {
            panel.BorderStyle = new Style(Color.DeepSkyBlue1);
        }

        _console.Write(panel);
        _console.WriteLine();
        return Task.CompletedTask;
    }

    public async Task<CliMenuMainSelection> SelectGroupAsync(
        CliMenuModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_mainMenuShown)
        {
            _console.Clear();
        }
        else
        {
            _mainMenuShown = true;
        }

        var choices = model.Groups
            .Select(group => new MenuDisplayChoice<CliMenuMainSelection>(
                group.Title,
                new CliMenuMainSelection(CliMenuMainAction.OpenGroup, group)))
            .Append(new MenuDisplayChoice<CliMenuMainSelection>(
                _text.Get("MenuExit"),
                new CliMenuMainSelection(CliMenuMainAction.Exit)))
            .ToArray();
        var cancelled = new MenuDisplayChoice<CliMenuMainSelection>(
            string.Empty,
            new CliMenuMainSelection(CliMenuMainAction.Cancel));

        return (await PromptAsync(
                _text.Get("MenuTitle"),
                choices,
                cancelled,
                cancellationToken)
            .ConfigureAwait(false)).Value;
    }

    public async Task<CliMenuCommandSelection> SelectCommandAsync(
        CliMenuGroup group,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        _console.Clear();
        var choices = group.Commands
            .Select(command => new MenuDisplayChoice<CliMenuCommandSelection>(
                $"{command.Name} - {_text.Get(command.TitleResourceKey)}",
                new CliMenuCommandSelection(CliMenuCommandAction.ShowDetails, command)))
            .Append(new MenuDisplayChoice<CliMenuCommandSelection>(
                _text.Get("MenuBack"),
                new CliMenuCommandSelection(CliMenuCommandAction.Back)))
            .ToArray();
        var cancelled = new MenuDisplayChoice<CliMenuCommandSelection>(
            string.Empty,
            new CliMenuCommandSelection(CliMenuCommandAction.Cancel));

        return (await PromptAsync(
                $"{group.Title}: {_text.Get("MenuCommandTitle")}",
                choices,
                cancelled,
                cancellationToken)
            .ConfigureAwait(false)).Value;
    }

    public async Task<CliMenuDetailsAction> ShowDetailsAsync(
        CliMenuDetails details,
        bool assistantAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        _console.Clear();
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        AddDetailRow(grid, "MenuDetailsDescription", details.Description);
        AddDetailRow(grid, "MenuDetailsUsage", details.Usage);
        AddDetailRow(grid, "MenuDetailsInputs", string.Join(Environment.NewLine, details.RequiredInputs));
        AddDetailRow(grid, "MenuDetailsOutputs", details.FileEffects);
        AddDetailRow(grid, "MenuDetailsSecurity", string.Join(Environment.NewLine, details.SecurityNotes));

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            Expand = true,
            Header = new PanelHeader(CliMarkup.EscapeExternal(
                _text.Format("MenuDetailsTitle", details.CommandName))),
        };
        _console.Write(panel);

        var choices = assistantAvailable
            ? new[]
            {
                new MenuDisplayChoice<CliMenuDetailsAction>(
                    _text.Get("AssistantConfigureAndRun"),
                    CliMenuDetailsAction.RunAssistant),
                new MenuDisplayChoice<CliMenuDetailsAction>(
                    _text.Get("MenuReturnFromDetails"),
                    CliMenuDetailsAction.Back),
            }
            :
            [
                new MenuDisplayChoice<CliMenuDetailsAction>(
                    _text.Get("MenuReturnFromDetails"),
                    CliMenuDetailsAction.Back),
            ];
        var cancelled = new MenuDisplayChoice<CliMenuDetailsAction>(string.Empty, CliMenuDetailsAction.Cancel);
        return (await PromptAsync(
                _text.Get("MenuDetailsPrompt"),
                choices,
                cancelled,
                cancellationToken)
            .ConfigureAwait(false)).Value;
    }

    public async Task<CliAssistantTextResult> PromptTextAsync(
        string title,
        string cancellationHint,
        CancellationToken cancellationToken)
    {
        _console.Clear();
        var prompt = new TextPrompt<string>(
                $"[bold]{CliMarkup.EscapeExternal(title)}[/]{Environment.NewLine}"
                + $"[dim]{CliMarkup.EscapeExternal(cancellationHint)}[/]{Environment.NewLine}> ")
            .AllowEmpty();
        var value = await _console.PromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), ":cancel", StringComparison.OrdinalIgnoreCase)
            ? CliAssistantTextResult.Cancelled
            : CliAssistantTextResult.Entered(value);
    }

    public async Task<CliAssistantChoiceResult> SelectOneAsync(
        string title,
        ImmutableArray<CliAssistantChoice> choices,
        CancellationToken cancellationToken)
    {
        if (_preserveNextPrompt)
        {
            _preserveNextPrompt = false;
        }
        else
        {
            _console.Clear();
        }

        var displayChoices = choices.Select(choice => new MenuDisplayChoice<CliAssistantChoiceResult>(
            FormatAssistantChoice(choice),
            new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, choice.Id)));
        var cancelled = new MenuDisplayChoice<CliAssistantChoiceResult>(
            string.Empty,
            new CliAssistantChoiceResult(CliAssistantChoiceAction.Cancel));
        return (await PromptAsync(title, displayChoices, cancelled, cancellationToken).ConfigureAwait(false)).Value;
    }

    public async Task<CliAssistantMultiChoiceResult> SelectManyAsync(
        string title,
        ImmutableArray<CliAssistantChoice> choices,
        CancellationToken cancellationToken)
    {
        _console.Clear();
        var cancelled = new AssistantMultiChoice("__cancel", string.Empty);
        var prompt = new MultiSelectionPrompt<AssistantMultiChoice>()
            .Title($"[bold]{CliMarkup.EscapeExternal(title)}[/]{Environment.NewLine}"
                   + $"[dim]{CliMarkup.EscapeExternal(_text.Get("AssistantMultiSelectionHint"))}[/]")
            .NotRequired()
            .PageSize(PageSize)
            .MoreChoicesText(CliMarkup.EscapeExternal(_text.Get("MenuMoreChoices")))
            .InstructionsText(CliMarkup.EscapeExternal(_text.Get("AssistantMultiSelectionInstructions")))
            .UseConverter(choice => choice.Label)
            .AddChoices(choices.Select(choice => new AssistantMultiChoice(
                choice.Id,
                CliMarkup.EscapeExternal(FormatAssistantChoice(choice)))))
            .AddCancelResult(cancelled);
        var selected = await _console.PromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        return selected.Any(choice => choice.Id == cancelled.Id)
            ? new CliAssistantMultiChoiceResult(true, [])
            : new CliAssistantMultiChoiceResult(false, [.. selected.Select(static choice => choice.Id)]);
    }

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var panel = new Panel(new Text(message))
        {
            Header = new PanelHeader(CliMarkup.EscapeExternal(title)),
            Border = BoxBorder.Rounded,
            Expand = true,
        };
        if (_useColor)
        {
            panel.BorderStyle = new Style(Color.Yellow);
        }

        _console.Write(panel);
        return Task.CompletedTask;
    }

    public Task ShowSummaryAsync(CliAssistantSummary summary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _console.Clear();
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        foreach (var (label, value) in summary.Rows)
        {
            grid.AddRow(
                new Markup(_useColor
                    ? $"[bold deepskyblue1]{CliMarkup.EscapeExternal(label)}[/]"
                    : $"[bold]{CliMarkup.EscapeExternal(label)}[/]"),
                new Text(value));
        }

        var contentRows = new List<IRenderable> { grid };
        contentRows.AddRange(summary.Warnings.Select(warning => (IRenderable)new Markup(_useColor
            ? $"[yellow]{CliMarkup.EscapeExternal(warning)}[/]"
            : CliMarkup.EscapeExternal(warning))));
        var content = new Rows(contentRows);
        var panel = new Panel(content)
        {
            Header = new PanelHeader(CliMarkup.EscapeExternal(summary.Title)),
            Border = BoxBorder.Rounded,
            Expand = true,
        };
        _console.Write(panel);
        _preserveNextPrompt = true;
        return Task.CompletedTask;
    }

    public Task ShowExecutionResultAsync(
        CliMenuExecutionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _console.WriteLine();
        var outputLines = result.OutputLines.IsDefault ? [] : result.OutputLines;
        var diagnosticLines = result.DiagnosticLines.IsDefault ? [] : result.DiagnosticLines;
        var succeeded = result.ExitCode == 0;
        var statusText = _text.Get(succeeded ? "AssistantResultSucceeded" : "AssistantResultFailed");
        var statusSymbol = succeeded ? "[OK]" : "[ERROR]";
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty).NoWrap())
            .AddColumn(new TableColumn(string.Empty));
        table.AddRow(
            ResultLabel("AssistantResultStatus"),
            new Markup(_useColor
                ? $"[bold {(succeeded ? "green" : "red")}]{CliMarkup.EscapeExternal(statusSymbol)} "
                  + $"{CliMarkup.EscapeExternal(statusText)}[/]"
                : $"[bold]{CliMarkup.EscapeExternal(statusSymbol)} {CliMarkup.EscapeExternal(statusText)}[/]"));
        table.AddRow(
            ResultLabel("AssistantResultExitCode"),
            new Text(result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        table.AddRow(
            ResultLabel("AssistantResultDuration"),
            new Text(_text.Format("AssistantResultDurationValue", result.Duration.TotalMilliseconds)));
        table.AddRow(
            ResultLabel("AssistantResultFileCount"),
            new Text(result.WrittenFiles.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        table.AddRow(
            ResultLabel("AssistantResultOutputCount"),
            new Text(outputLines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        table.AddRow(
            ResultLabel("AssistantResultDiagnosticCount"),
            new Text(diagnosticLines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var rows = new List<IRenderable> { table };
        if (!result.WrittenFiles.IsEmpty)
        {
            rows.Add(new Markup($"[bold]{CliMarkup.EscapeExternal(_text.Get("AssistantFilesWritten"))}[/]"));
            rows.AddRange(result.WrittenFiles.Select(static path => (IRenderable)new Text($"  {path}")));
        }

        if (!outputLines.IsEmpty)
        {
            rows.Add(new Panel(new Text(string.Join(Environment.NewLine, outputLines)))
            {
                Header = new PanelHeader(CliMarkup.EscapeExternal(_text.Get("AssistantResultOutputTitle"))),
                Border = BoxBorder.Rounded,
                Expand = true,
            });
        }

        if (!diagnosticLines.IsEmpty)
        {
            var diagnostics = new Panel(new Text(string.Join(Environment.NewLine, diagnosticLines)))
            {
                Header = new PanelHeader(CliMarkup.EscapeExternal(_text.Get("AssistantResultDiagnosticsTitle"))),
                Border = BoxBorder.Rounded,
                Expand = true,
            };
            if (_useColor)
            {
                diagnostics.BorderStyle = new Style(Color.Red);
            }

            rows.Add(diagnostics);
        }

        _console.Write(new Panel(new Rows(rows))
        {
            Header = new PanelHeader(CliMarkup.EscapeExternal(_text.Get("AssistantResultTitle"))),
            Border = BoxBorder.Rounded,
            Expand = true,
        });
        return Task.CompletedTask;
    }

    public Task<CliMenuExecutionResult> ExecuteCommandAsync(
        CliCommand command,
        CliMenuCommandExecutor executor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(executor);
        _console.Clear();
        return _console.Progress()
            .AutoClear(true)
            .HideCompleted(false)
            .Columns(
                new SpinnerColumn(),
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask(
                    CliMarkup.EscapeExternal(_text.Get("ProgressWaiting")),
                    autoStart: true,
                    maxValue: 1);
                task.IsIndeterminate = true;
                var progress = new SpectreOperationProgress(task, _text);
                return await executor(command, progress, cancellationToken).ConfigureAwait(false);
            });
    }

    public async Task WaitForReturnAsync(CancellationToken cancellationToken)
    {
        var back = new MenuDisplayChoice<bool>(_text.Get("AssistantReturnToMenu"), true);
        var cancelled = new MenuDisplayChoice<bool>(string.Empty, false);
        await PromptAsync(_text.Get("AssistantAfterExecutionPrompt"), [back], cancelled, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MenuDisplayChoice<T>> PromptAsync<T>(
        string title,
        IEnumerable<MenuDisplayChoice<T>> choices,
        MenuDisplayChoice<T> cancelResult,
        CancellationToken cancellationToken)
    {
        var prompt = new SelectionPrompt<MenuDisplayChoice<T>>()
            .Title($"[bold]{CliMarkup.EscapeExternal(title)}[/]{Environment.NewLine}"
                   + $"[dim]{CliMarkup.EscapeExternal(_text.Get("MenuNavigationHint"))}[/]")
            .PageSize(PageSize)
            .WrapAround(true)
            .MoreChoicesText(CliMarkup.EscapeExternal(_text.Get("MenuMoreChoices")))
            .UseConverter(choice => CliMarkup.EscapeExternal(choice.Label))
            .AddChoices(choices)
            .AddCancelResult(cancelResult);

        return await _console.PromptAsync(prompt, cancellationToken).ConfigureAwait(false);
    }

    private void AddDetailRow(Grid grid, string labelResourceKey, string value)
    {
        var label = CliMarkup.EscapeExternal(_text.Get(labelResourceKey));
        grid.AddRow(
            new Markup(_useColor ? $"[bold deepskyblue1]{label}[/]" : $"[bold]{label}[/]"),
            new Text(value));
    }

    private static string FormatAssistantChoice(CliAssistantChoice choice) =>
        choice.Description is null ? choice.Label : $"{choice.Label} - {choice.Description}";

    private Markup ResultLabel(string resourceKey)
    {
        var label = CliMarkup.EscapeExternal(_text.Get(resourceKey));
        return new Markup(_useColor ? $"[bold deepskyblue1]{label}[/]" : $"[bold]{label}[/]");
    }

    private sealed class SpectreOperationProgress(ProgressTask task, CliTextCatalog text) : ICliOperationProgress
    {
        private readonly ProgressTask _task = task ?? throw new ArgumentNullException(nameof(task));
        private readonly CliTextCatalog _text = text ?? throw new ArgumentNullException(nameof(text));

        public void Report(CliOperationProgressUpdate update)
        {
            ArgumentNullException.ThrowIfNull(update);
            var stateKey = update.State switch
            {
                CliOperationProgressState.Started => "ProgressStarted",
                CliOperationProgressState.Advanced => "ProgressRunning",
                CliOperationProgressState.Completed => "ProgressCompleted",
                CliOperationProgressState.Failed => "ProgressFailed",
                _ => throw new ArgumentOutOfRangeException(nameof(update)),
            };
            _task.Description = CliMarkup.EscapeExternal(
                _text.Format(stateKey, _text.Get(update.MessageResourceKey)));
            if (update.TotalUnits.HasValue)
            {
                _task.IsIndeterminate = false;
                _task.MaxValue = update.TotalUnits.Value;
                _task.Value = update.CompletedUnits!.Value;
            }
            else if (update.State is CliOperationProgressState.Completed or CliOperationProgressState.Failed)
            {
                _task.IsIndeterminate = false;
                _task.MaxValue = 1;
                _task.Value = 1;
            }
            else
            {
                _task.IsIndeterminate = true;
            }
        }
    }

    private sealed record MenuDisplayChoice<T>(string Label, T Value);

    private sealed record AssistantMultiChoice(string Id, string Label);
}
