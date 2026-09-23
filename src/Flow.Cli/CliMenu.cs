using System.Collections.Immutable;
using Spectre.Console;

namespace Flow.Cli;

internal static class CliMenuDiagnosticCodes
{
    public const string RequiresInteractiveTerminal = "FLOWCLI_MENU_REQUIRES_INTERACTIVE";
}

internal sealed record CliMenuGroup(
    CliCommandGroup Group,
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
            text.Get("HelpTitle"),
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

        return new CliMenuModel(
        [
            .. Enum.GetValues<CliCommandGroup>()
                .Select(group => new CliMenuGroup(
                    group,
                    text.Get(CliCommandGroupResources.GetTitleKey(group)),
                    [.. CliCommandCatalog.All.Where(command =>
                        command.Group == group && command.AvailableInInteractiveMenu)]))
                .Where(group => !group.Commands.IsEmpty),
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

    public Task<bool> ShowDetailsAsync(
        CliMenuDetails details,
        CancellationToken cancellationToken);
}

internal interface ICliMenu
{
    public Task<int> RunAsync(
        TextWriter output,
        CliTextCatalog text,
        CliPresentationProfile presentation,
        CancellationToken cancellationToken);
}

internal sealed class CliMenuController(ICliMenuView view)
{
    private readonly ICliMenuView _view = view ?? throw new ArgumentNullException(nameof(view));

    public async Task<int> RunAsync(
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(text);

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
                var returnToCommands = await _view.ShowDetailsAsync(
                        CliMenuDetails.Create(command, text),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!returnToCommands)
                {
                    await output.WriteLineAsync(text.Get("MenuCancelled")).ConfigureAwait(false);
                    return 0;
                }
            }
        }
    }
}

internal sealed class SpectreCliMenu : ICliMenu
{
    public Task<int> RunAsync(
        TextWriter output,
        CliTextCatalog text,
        CliPresentationProfile presentation,
        CancellationToken cancellationToken)
    {
        var view = new SpectreCliMenuView(output, text, presentation);
        return new CliMenuController(view).RunAsync(output, text, cancellationToken);
    }
}

internal sealed class SpectreCliMenuView : ICliMenuView
{
    private const int PageSize = 8;
    private readonly IAnsiConsole _console;
    private readonly CliTextCatalog _text;
    private readonly bool _useColor;
    private bool _mainMenuShown;

    public SpectreCliMenuView(
        TextWriter output,
        CliTextCatalog text,
        CliPresentationProfile presentation)
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
            Out = new CliAnsiConsoleOutput(output, presentation.Width),
        });
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

    public async Task<bool> ShowDetailsAsync(
        CliMenuDetails details,
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

        var back = new MenuDisplayChoice<bool>(_text.Get("MenuReturnFromDetails"), true);
        var cancelled = new MenuDisplayChoice<bool>(string.Empty, false);
        return (await PromptAsync(
                _text.Get("MenuDetailsPrompt"),
                [back],
                cancelled,
                cancellationToken)
            .ConfigureAwait(false)).Value;
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

    private sealed record MenuDisplayChoice<T>(string Label, T Value);
}
