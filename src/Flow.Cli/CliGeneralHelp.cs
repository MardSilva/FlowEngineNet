using System.Collections.Immutable;
using System.Text;
using Spectre.Console;

namespace Flow.Cli;

internal sealed record CliGeneralHelpOption(string Syntax, string Description);

internal sealed record CliGeneralHelpCommand(string Name, string Summary);

internal sealed record CliGeneralHelpGroup(
    CliCommandGroup Group,
    string Title,
    ImmutableArray<CliGeneralHelpCommand> Commands);

internal sealed record CliGeneralHelpModel(
    string Title,
    string Warning,
    string GlobalOptionsTitle,
    ImmutableArray<CliGeneralHelpOption> GlobalOptions,
    string CommandsTitle,
    ImmutableArray<CliGeneralHelpGroup> Groups,
    string SpecificHelp,
    string ExitCodes)
{
    public static CliGeneralHelpModel Create(CliTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var groups = Enum.GetValues<CliCommandGroup>()
            .Select(group => new CliGeneralHelpGroup(
                group,
                text.Get(CliCommandGroupResources.GetTitleKey(group)),
                [.. CliCommandCatalog.All
                    .Where(command => command.Group == group)
                    .Select(command => new CliGeneralHelpCommand(
                        command.Name,
                        text.Get(command.DescriptionResourceKey)))]))
            .ToImmutableArray();

        return new CliGeneralHelpModel(
            text.Format("HelpTitle", CliProductInfo.Version),
            text.Get("HelpWarning"),
            text.Get("HelpGlobalOptions"),
            [
                new("--language <en-US|pt-BR>", text.Get("HelpLanguageDescription")),
                new("--banner", text.Get("HelpBannerDescription")),
                new("--no-color", text.Get("HelpNoColorDescription")),
                new("--plain", text.Get("HelpPlainDescription")),
            ],
            text.Get("HelpCommands"),
            groups,
            text.Get("HelpSpecific"),
            text.Get("HelpExitCodes"));
    }

}

internal static class CliGeneralHelpWriter
{
    private const int PlainWidth = 80;
    private const int NarrowRichWidth = 64;

    public static async Task WriteAsync(
        TextWriter output,
        CliTextCatalog text,
        CliPresentationProfile presentation)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(presentation);

        var model = CliGeneralHelpModel.Create(text);
        if (presentation.Mode == CliPresentationMode.Rich)
        {
            WriteRich(output, model, presentation);
            return;
        }

        await WritePlainAsync(output, model).ConfigureAwait(false);
    }

    private static async Task WritePlainAsync(TextWriter output, CliGeneralHelpModel model)
    {
        await WriteWrappedAsync(output, model.Title, PlainWidth).ConfigureAwait(false);
        await WriteWrappedAsync(output, model.Warning, PlainWidth).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(model.GlobalOptionsTitle).ConfigureAwait(false);
        foreach (var option in model.GlobalOptions)
        {
            await WriteWrappedAsync(output, option.Syntax, PlainWidth, 2).ConfigureAwait(false);
            await WriteWrappedAsync(output, option.Description, PlainWidth, 4).ConfigureAwait(false);
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(model.CommandsTitle).ConfigureAwait(false);
        foreach (var group in model.Groups)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync(group.Title).ConfigureAwait(false);
            foreach (var command in group.Commands)
            {
                await WriteWrappedAsync(output, command.Name, PlainWidth, 2).ConfigureAwait(false);
                await WriteWrappedAsync(output, command.Summary, PlainWidth, 4).ConfigureAwait(false);
            }
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await WriteWrappedAsync(output, model.SpecificHelp, PlainWidth).ConfigureAwait(false);
        await WriteWrappedAsync(output, model.ExitCodes, PlainWidth).ConfigureAwait(false);
    }

    private static void WriteRich(
        TextWriter output,
        CliGeneralHelpModel model,
        CliPresentationProfile presentation)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = presentation.UseColor ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = presentation.UseColor ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            // Keep CI environment detection from overriding the resolved presentation profile.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new CliAnsiConsoleOutput(output, presentation.Width),
        });

        console.MarkupLine(Styled(model.Title, "bold deepskyblue1", presentation.UseColor));
        console.MarkupLine(Styled(model.Warning, "yellow", presentation.UseColor));
        console.WriteLine();
        console.MarkupLine(Styled(model.GlobalOptionsTitle, "bold", presentation.UseColor));
        console.Write(CreateGrid(
            model.GlobalOptions.Select(option => (option.Syntax, option.Description)),
            presentation));
        console.WriteLine();
        console.MarkupLine(Styled(model.CommandsTitle, "bold", presentation.UseColor));

        foreach (var group in model.Groups)
        {
            console.WriteLine();
            console.MarkupLine(Styled(group.Title, "bold deepskyblue1", presentation.UseColor));
            console.Write(CreateGrid(
                group.Commands.Select(command => (command.Name, command.Summary)),
                presentation));
        }

        console.WriteLine();
        console.MarkupLine(Styled(model.SpecificHelp, "dim", presentation.UseColor));
        console.MarkupLine(Styled(model.ExitCodes, "dim", presentation.UseColor));
    }

    private static Grid CreateGrid(
        IEnumerable<(string Name, string Description)> entries,
        CliPresentationProfile presentation)
    {
        var grid = new Grid();
        if (presentation.Width < NarrowRichWidth)
        {
            grid.AddColumn();
            foreach (var entry in entries)
            {
                grid.AddRow(new Markup($"[bold]{CliMarkup.EscapeExternal(entry.Name)}[/]"));
                grid.AddRow(new Padder(new Text(entry.Description), new Padding(2, 0, 0, 0)));
            }

            return grid;
        }

        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        foreach (var entry in entries)
        {
            grid.AddRow(
                new Markup($"[bold]{CliMarkup.EscapeExternal(entry.Name)}[/]"),
                new Text(entry.Description));
        }

        return grid;
    }

    private static string Styled(string value, string style, bool enabled) => enabled
        ? $"[{style}]{CliMarkup.EscapeExternal(value)}[/]"
        : CliMarkup.EscapeExternal(value);

    private static async Task WriteWrappedAsync(
        TextWriter output,
        string value,
        int width,
        int indentation = 0)
    {
        foreach (var line in Wrap(value, width, indentation))
        {
            await output.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> Wrap(string value, int width, int indentation = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (width <= indentation)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var prefix = new string(' ', indentation);
        var available = width - indentation;
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [prefix];
        }

        var lines = new List<string>();
        var current = new StringBuilder(prefix);
        foreach (var word in words)
        {
            if (current.Length > indentation && current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(prefix);
            }

            if (current.Length > indentation)
            {
                current.Append(' ');
            }

            if (word.Length <= available)
            {
                current.Append(word);
                continue;
            }

            var remaining = word.AsSpan();
            while (remaining.Length > available)
            {
                if (current.Length > indentation)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(prefix);
                }

                lines.Add(prefix + remaining[..available].ToString());
                remaining = remaining[available..];
            }

            current.Append(remaining);
        }

        if (current.Length > indentation)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }
}

internal sealed class CliAnsiConsoleOutput : IAnsiConsoleOutput
{
    private readonly Func<int> _width;

    public CliAnsiConsoleOutput(TextWriter writer, int width)
        : this(writer, () => width)
    {
    }

    public CliAnsiConsoleOutput(TextWriter writer, Func<int> width)
    {
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _width = width ?? throw new ArgumentNullException(nameof(width));
    }

    public TextWriter Writer { get; }

    public bool IsTerminal => true;

    public int Width => _width();

    public int Height => 25;

    public void SetEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
    }
}
