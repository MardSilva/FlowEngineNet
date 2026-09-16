using System.Globalization;
using Flow.Rendering.Html;

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
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure($"FLOWCLI_USAGE: {ImportUsage}");
        }

        string? outputPath = null;
        string? diagnosticsJsonOutputPath = null;
        string? fidelityReportOutputPath = null;
        string? metadataJsonOutputPath = null;
        string? processingJsonOutputPath = null;
        string? sourceMapJsonOutputPath = null;
        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return CommandParseResult.Failure(
                    $"FLOWCLI_USAGE: Option '{arguments[index]}' requires a value. {ImportUsage}");
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--output" when outputPath is null:
                    outputPath = value;
                    break;
                case "--diagnostics-json" when diagnosticsJsonOutputPath is null:
                    diagnosticsJsonOutputPath = value;
                    break;
                case "--fidelity-report" when fidelityReportOutputPath is null:
                    fidelityReportOutputPath = value;
                    break;
                case "--metadata-json" when metadataJsonOutputPath is null:
                    metadataJsonOutputPath = value;
                    break;
                case "--processing-json" when processingJsonOutputPath is null:
                    processingJsonOutputPath = value;
                    break;
                case "--source-map-json" when sourceMapJsonOutputPath is null:
                    sourceMapJsonOutputPath = value;
                    break;
                case "--output" or "--diagnostics-json" or "--fidelity-report" or "--metadata-json"
                    or "--processing-json" or "--source-map-json":
                    return CommandParseResult.Failure(
                        $"FLOWCLI_DUPLICATE_OPTION: Option '{option}' was specified more than once.");
                default:
                    return CommandParseResult.Failure(
                        $"FLOWCLI_UNKNOWN_OPTION: Unknown import option '{option}'. {ImportUsage}");
            }
        }

        return CommandParseResult.Success(
            new ImportEpubCommand(
                arguments[1],
                outputPath,
                diagnosticsJsonOutputPath,
                fidelityReportOutputPath,
                metadataJsonOutputPath,
                processingJsonOutputPath,
                sourceMapJsonOutputPath));
    }

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

        if (arguments.Skip(2).Contains("--html-book", StringComparer.Ordinal))
        {
            return ParseHtmlBook(arguments);
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

    private static CommandParseResult ParseHtmlBook(IReadOnlyList<string> arguments)
    {
        string? outputDirectory = null;
        var uiLanguage = HtmlBookUiLanguage.Automatic;
        var hasUiLanguage = false;
        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return CommandParseResult.Failure(
                    $"FLOWCLI_USAGE: Option '{arguments[index]}' requires a value. {RenderUsage}");
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--html-book" when outputDirectory is null:
                    outputDirectory = value;
                    break;
                case "--ui-language" when !hasUiLanguage:
                    if (!TryParseUiLanguage(value, out uiLanguage))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE: --ui-language must be auto, en, pt-PT, or pt-BR.");
                    }

                    hasUiLanguage = true;
                    break;
                case "--html-book" or "--ui-language":
                    return CommandParseResult.Failure(
                        $"FLOWCLI_DUPLICATE_OPTION: Option '{option}' was specified more than once.");
                default:
                    return CommandParseResult.Failure(
                        $"FLOWCLI_UNKNOWN_OPTION: Unknown HTML book option '{option}'. {RenderUsage}");
            }
        }

        return outputDirectory is null
            ? CommandParseResult.Failure($"FLOWCLI_USAGE: {RenderUsage}")
            : CommandParseResult.Success(new RenderHtmlBookCommand(arguments[1], outputDirectory, uiLanguage));
    }

    private static bool TryParseUiLanguage(string value, out HtmlBookUiLanguage language)
    {
        language = value.ToLowerInvariant() switch
        {
            "auto" => HtmlBookUiLanguage.Automatic,
            "en" => HtmlBookUiLanguage.English,
            "pt-pt" => HtmlBookUiLanguage.PortuguesePortugal,
            "pt-br" => HtmlBookUiLanguage.PortugueseBrazil,
            _ => (HtmlBookUiLanguage)(-1),
        };
        return Enum.IsDefined(language);
    }

    private static bool TryParseDimension(string value, out double dimension) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dimension)
        && double.IsFinite(dimension)
        && dimension > 0;

    private const string RenderUsage =
        "Usage: flow render <document> (--html <output> --width <n> --height <n> | --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>])";

    private const string ImportUsage =
        "Usage: flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>] [--metadata-json <metadata.json>] [--processing-json <processing.json>] [--source-map-json <source-map.json>]";

    private const string EpubInspectUsage = "Usage: flow epub-inspect <book.epub> [--json <report.json>]";
}
