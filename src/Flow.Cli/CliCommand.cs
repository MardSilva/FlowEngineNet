using Flow.Rendering.Html;

namespace Flow.Cli;

public abstract record CliCommand;

public sealed record HelpCommand : CliCommand;

public sealed record SampleCommand(string OutputPath) : CliCommand;

public sealed record ImportEpubCommand(
    string SourcePath,
    string? OutputPath,
    string? DiagnosticsJsonOutputPath,
    string? FidelityReportOutputPath = null) : CliCommand;

public sealed record InspectEpubCommand(string SourcePath, string? JsonOutputPath) : CliCommand;

public sealed record InspectCommand(string DocumentPath) : CliCommand;

public sealed record ValidateCommand(string DocumentPath) : CliCommand;

public sealed record HashCommand(string DocumentPath) : CliCommand;

public sealed record RenderHtmlCommand(
    string DocumentPath,
    string OutputPath,
    double ViewportWidth,
    double ViewportHeight) : CliCommand;

public sealed record RenderHtmlBookCommand(
    string DocumentPath,
    string OutputDirectory,
    HtmlBookUiLanguage UiLanguage = HtmlBookUiLanguage.Automatic) : CliCommand;

public sealed record CommandParseResult
{
    private CommandParseResult(CliCommand? command, string? error)
    {
        Command = command;
        Error = error;
    }

    public CliCommand? Command { get; }

    public string? Error { get; }

    public bool IsSuccess => Command is not null;

    public static CommandParseResult Success(CliCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new CommandParseResult(command, null);
    }

    public static CommandParseResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CommandParseResult(null, error);
    }
}
