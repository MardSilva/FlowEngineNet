using System.Globalization;

namespace Flow.Cli;

/// <summary>Parses framework-free command-line arguments into typed Flow CLI commands.</summary>
public sealed class CliCommandParser
{
    /// <summary>Parses one complete argument vector.</summary>
    public CommandParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || arguments[0] is "help" or "--help" or "-h")
        {
            return CommandParseResult.Success(new HelpCommand());
        }

        return arguments[0] switch
        {
            "sample" => ParseSample(arguments),
            "import" => ParseImport(arguments),
            "epub-inspect" => ParseEpubInspect(arguments),
            "inspect" => ParseDocumentCommand(arguments, static path => new InspectCommand(path)),
            "validate" => ParseDocumentCommand(arguments, static path => new ValidateCommand(path)),
            "hash" => ParseDocumentCommand(arguments, static path => new HashCommand(path)),
            "render" => ParseRender(arguments),
            _ => CommandParseResult.Failure(
                $"FLOWCLI_UNKNOWN_COMMAND: Unknown command '{arguments[0]}'. Run 'flow help' for usage."),
        };
    }

    private static CommandParseResult ParseImport(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 2)
        {
            return CommandParseResult.Success(
                new ImportEpubCommand(arguments[1], DefaultImportOutputPath(arguments[1])));
        }

        if (arguments.Count == 4 && arguments[2] == "--output" && !string.IsNullOrWhiteSpace(arguments[3]))
        {
            return CommandParseResult.Success(new ImportEpubCommand(arguments[1], arguments[3]));
        }

        return CommandParseResult.Failure($"FLOWCLI_USAGE: {ImportUsage}");
    }

    private static string DefaultImportOutputPath(string sourcePath) =>
        sourcePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)
            ? $"{sourcePath[..^5]}.flow.json"
            : $"{sourcePath}.flow.json";

    private static CommandParseResult ParseEpubInspect(IReadOnlyList<string> arguments) =>
        arguments.Count switch
        {
            2 => CommandParseResult.Success(new InspectEpubCommand(arguments[1], null)),
            4 when arguments[2] == "--json" && !string.IsNullOrWhiteSpace(arguments[3]) =>
                CommandParseResult.Success(new InspectEpubCommand(arguments[1], arguments[3])),
            _ => CommandParseResult.Failure($"FLOWCLI_USAGE: {EpubInspectUsage}"),
        };

    private static CommandParseResult ParseSample(IReadOnlyList<string> arguments) =>
        arguments.Count switch
        {
            1 => CommandParseResult.Success(new SampleCommand("sample.flow.json")),
            2 => CommandParseResult.Success(new SampleCommand(arguments[1])),
            _ => CommandParseResult.Failure("FLOWCLI_USAGE: Usage: flow sample [output]"),
        };

    private static CommandParseResult ParseDocumentCommand(
        IReadOnlyList<string> arguments,
        Func<string, CliCommand> create) =>
        arguments.Count == 2
            ? CommandParseResult.Success(create(arguments[1]))
            : CommandParseResult.Failure($"FLOWCLI_USAGE: Usage: flow {arguments[0]} <document>");

    private static CommandParseResult ParseRender(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure($"FLOWCLI_USAGE: {RenderUsage}");
        }

        string? outputPath = null;
        double? width = null;
        double? height = null;

        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                return CommandParseResult.Failure(
                    $"FLOWCLI_USAGE: Option '{arguments[index]}' requires a value. {RenderUsage}");
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--html" when outputPath is null:
                    outputPath = value;
                    break;
                case "--width" when width is null:
                    if (!TryParseDimension(value, out var parsedWidth))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE: --width must be a finite number greater than zero.");
                    }

                    width = parsedWidth;
                    break;
                case "--height" when height is null:
                    if (!TryParseDimension(value, out var parsedHeight))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE: --height must be a finite number greater than zero.");
                    }

                    height = parsedHeight;
                    break;
                case "--html" or "--width" or "--height":
                    return CommandParseResult.Failure(
                        $"FLOWCLI_DUPLICATE_OPTION: Option '{option}' was specified more than once.");
                default:
                    return CommandParseResult.Failure(
                        $"FLOWCLI_UNKNOWN_OPTION: Unknown render option '{option}'. {RenderUsage}");
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath) || width is null || height is null)
        {
            return CommandParseResult.Failure($"FLOWCLI_USAGE: {RenderUsage}");
        }

        return CommandParseResult.Success(
            new RenderHtmlCommand(arguments[1], outputPath, width.Value, height.Value));
    }

    private static bool TryParseDimension(string value, out double dimension) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dimension)
        && double.IsFinite(dimension)
        && dimension > 0;

    private const string RenderUsage =
        "Usage: flow render <document> --html <output> --width <n> --height <n>";

    private const string ImportUsage = "Usage: flow import <book.epub> [--output <book.flow.json>]";

    private const string EpubInspectUsage = "Usage: flow epub-inspect <book.epub> [--json <report.json>]";
}
