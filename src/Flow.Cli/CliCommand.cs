using Flow.Epub;
using Flow.Rendering.Html;

namespace Flow.Cli;

public abstract record CliCommand;

public sealed record HelpCommand(string? CommandName = null) : CliCommand;

/// <summary>Requests the read-only interactive command browser.</summary>
public sealed record MenuCommand : CliCommand;

public sealed record SampleCommand(string OutputPath) : CliCommand;

public sealed record ImportEpubCommand(
    string SourcePath,
    string? OutputPath,
    string? DiagnosticsJsonOutputPath,
    string? FidelityReportOutputPath = null,
    string? MetadataJsonOutputPath = null,
    string? ProcessingJsonOutputPath = null,
    string? SourceMapJsonOutputPath = null) : CliCommand;

public sealed record InspectEpubCommand(string SourcePath, string? JsonOutputPath) : CliCommand;

public sealed record InventoryEpubCommand(
    string SourceDirectory,
    string OutputPath,
    string RepositoryRoot,
    bool Force = false) : CliCommand;

public sealed record QualifyEpubInventoryCommand(
    string SourceDirectory,
    string ReportPath,
    string RepositoryRoot,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record ClassifyEpubInventoryCommand(
    string QualificationReportPath,
    EpubCorpusSha256 ExpectedQualificationSha256,
    string OutputPath,
    string RepositoryRoot,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record ReviewEpubInventoryCommand(
    string SourceDirectory,
    string QualificationReportPath,
    EpubCorpusSha256 ExpectedQualificationSha256,
    string OutputDirectory,
    string RepositoryRoot,
    HtmlBookUiLanguage UiLanguage,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record CorpusCommand(
    string ManifestPath,
    string RepositoryRoot,
    string ReportPath,
    string? ExternalCorpusRoot,
    string? AcceptedBaselinePath,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record QualifyEpubCommand(
    string SourcePath,
    EpubCorpusPublicationId CandidateId,
    EpubCorpusSha256 ExpectedSourceSha256,
    string ReportPath,
    string RepositoryRoot,
    int RepetitionCount,
    bool IncludeEnvironment,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record ReviewEpubCommand(
    string SourcePath,
    EpubCorpusPublicationId CandidateId,
    EpubCorpusSha256 ExpectedSourceSha256,
    string OutputDirectory,
    string RepositoryRoot,
    HtmlBookUiLanguage UiLanguage,
    bool Force = false,
    bool Resume = false) : CliCommand;

public sealed record ExecutionStatusCommand(
    string DestinationPath,
    string? JsonOutputPath,
    bool Force = false) : CliCommand;

public sealed record ExecutionCleanCommand(string DestinationPath, Guid ExecutionId) : CliCommand;

/// <summary>Selects which class of official Flow releases may be considered.</summary>
public enum UpdateChannel
{
    Stable,
    Prerelease,
}

/// <summary>Requests an explicit, read-only check of the official release source.</summary>
public sealed record UpdateCheckCommand(
    UpdateChannel Channel = UpdateChannel.Stable,
    bool Json = false) : CliCommand;

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
    private readonly string? legacyError;

    private CommandParseResult(CliCommand? command, CliParseDiagnostic? diagnostic, string? legacyError = null)
    {
        Command = command;
        Diagnostic = diagnostic;
        this.legacyError = legacyError;
    }

    public CliCommand? Command { get; }

    public CliParseDiagnostic? Diagnostic { get; }

    public string? Error => legacyError ?? Diagnostic?.Format(new CliTextCatalog());

    public bool IsSuccess => Command is not null;

    public static CommandParseResult Success(CliCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new CommandParseResult(command, null);
    }

    public static CommandParseResult Failure(string code, string resourceKey, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return new CommandParseResult(null, new CliParseDiagnostic(code, resourceKey, arguments));
    }

    /// <summary>Creates a legacy literal parse failure. New parsers should use the structured overload.</summary>
    public static CommandParseResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CommandParseResult(null, null, error);
    }
}

public sealed record CliParseDiagnostic(string Code, string ResourceKey, IReadOnlyList<object?> Arguments)
{
    public string Format(CliTextCatalog text) => text.Diagnostic(Code, ResourceKey, Arguments.ToArray());
}
