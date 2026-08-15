using System.Globalization;

namespace Flow.Cli;

public sealed class CliCommandParser
{
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
            "inspect" => ParseDocumentCommand(arguments, static path => new InspectCommand(path)),
            "validate" => ParseDocumentCommand(arguments, static path => new ValidateCommand(path)),
            "hash" => ParseDocumentCommand(arguments, static path => new HashCommand(path)),
            "render" => ParseRender(arguments),
            _ => CommandParseResult.Failure($"Unknown command '{arguments[0]}'."),
        };
    }

    private static CommandParseResult ParseSample(IReadOnlyList<string> arguments) =>
        arguments.Count switch
        {
            1 => CommandParseResult.Success(new SampleCommand("sample.flow.json")),
            2 => CommandParseResult.Success(new SampleCommand(arguments[1])),
            _ => CommandParseResult.Failure("Usage: flow sample [output]"),
        };

    private static CommandParseResult ParseDocumentCommand(
        IReadOnlyList<string> arguments,
        Func<string, CliCommand> create) =>
        arguments.Count == 2
            ? CommandParseResult.Success(create(arguments[1]))
            : CommandParseResult.Failure($"Usage: flow {arguments[0]} <document>");

    private static CommandParseResult ParseRender(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure(RenderUsage);
        }

        string? outputPath = null;
        double? width = null;
        double? height = null;

        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                return CommandParseResult.Failure($"Option '{arguments[index]}' requires a value. {RenderUsage}");
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
                        return CommandParseResult.Failure("--width must be a finite number greater than zero.");
                    }

                    width = parsedWidth;
                    break;
                case "--height" when height is null:
                    if (!TryParseDimension(value, out var parsedHeight))
                    {
                        return CommandParseResult.Failure("--height must be a finite number greater than zero.");
                    }

                    height = parsedHeight;
                    break;
                case "--html" or "--width" or "--height":
                    return CommandParseResult.Failure($"Option '{option}' was specified more than once.");
                default:
                    return CommandParseResult.Failure($"Unknown render option '{option}'. {RenderUsage}");
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath) || width is null || height is null)
        {
            return CommandParseResult.Failure(RenderUsage);
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
}
