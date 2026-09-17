using System.Collections.Immutable;

namespace Flow.Epub.Corpus;

/// <summary>Identifies how a locally configured EPUBCheck installation is launched.</summary>
public enum EpubCheckToolKind
{
    Executable,
    Jar,
}

/// <summary>Classifies external EPUB conformance evidence independently of the Flow result.</summary>
public enum EpubCheckEvidenceStatus
{
    Disabled,
    Conformant,
    NonConformant,
    Unavailable,
    IncompatibleVersion,
    TimedOut,
    OutputLimitExceeded,
    UnrecognizedOutput,
    ToolError,
}

/// <summary>Explains whether Flow and EPUBCheck reached comparable outcomes.</summary>
public enum EpubCorpusEvidenceRelationship
{
    NotEvaluated,
    AgreePassed,
    AgreeFailed,
    FlowPassedEpubCheckFailed,
    FlowFailedEpubCheckPassed,
}

/// <summary>Configures a trusted local EPUBCheck command without shell evaluation.</summary>
public sealed record EpubCheckProcessOptions
{
    public EpubCheckProcessOptions(
        EpubCheckToolKind toolKind,
        string toolPath,
        string? javaExecutablePath = null,
        IEnumerable<string>? toolArguments = null,
        TimeSpan? timeout = null,
        int maximumCapturedCharacters = 256 * 1024,
        int minimumSupportedMajorVersion = 5,
        int maximumSupportedMajorVersion = 5)
    {
        if (!Enum.IsDefined(toolKind))
        {
            throw new ArgumentOutOfRangeException(nameof(toolKind));
        }

        ValidateAbsolutePath(toolPath, nameof(toolPath));
        if (toolKind == EpubCheckToolKind.Jar)
        {
            ValidateAbsolutePath(javaExecutablePath, nameof(javaExecutablePath));
        }
        else if (javaExecutablePath is not null)
        {
            throw new ArgumentException("A Java executable is valid only for JAR invocation.", nameof(javaExecutablePath));
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(2);
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCapturedCharacters, 1024);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSupportedMajorVersion);
        if (maximumSupportedMajorVersion < minimumSupportedMajorVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSupportedMajorVersion));
        }

        ToolKind = toolKind;
        ToolPath = Path.GetFullPath(toolPath);
        JavaExecutablePath = javaExecutablePath is null ? null : Path.GetFullPath(javaExecutablePath);
        ToolArguments = (toolArguments ?? []).ToImmutableArray();
        Timeout = effectiveTimeout;
        MaximumCapturedCharacters = maximumCapturedCharacters;
        MinimumSupportedMajorVersion = minimumSupportedMajorVersion;
        MaximumSupportedMajorVersion = maximumSupportedMajorVersion;
    }

    public EpubCheckToolKind ToolKind { get; }

    public string ToolPath { get; }

    public string? JavaExecutablePath { get; }

    public ImmutableArray<string> ToolArguments { get; }

    public TimeSpan Timeout { get; }

    public int MaximumCapturedCharacters { get; }

    public int MinimumSupportedMajorVersion { get; }

    public int MaximumSupportedMajorVersion { get; }

    private static void ValidateAbsolutePath(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("Tool paths must be explicit absolute paths.", parameterName);
        }
    }
}

/// <summary>Contains one normalized EPUBCheck message without a machine-local path.</summary>
public sealed record EpubCheckMessageEvidence(string Code, string Severity, string Message, string? Resource = null);

/// <summary>Contains bounded external conformance evidence and captured, sanitized process output.</summary>
public sealed record EpubCheckEvidence
{
    public EpubCheckEvidence(
        EpubCheckEvidenceStatus status,
        string? toolVersion = null,
        int? exitCode = null,
        int fatalCount = 0,
        int errorCount = 0,
        int warningCount = 0,
        int usageCount = 0,
        IEnumerable<EpubCheckMessageEvidence>? messages = null,
        IEnumerable<EpubCorpusExecutionDiagnostic>? diagnostics = null,
        string? standardOutput = null,
        string? standardError = null)
    {
        Status = status;
        ToolVersion = toolVersion;
        ExitCode = exitCode;
        FatalCount = fatalCount;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        UsageCount = usageCount;
        Messages = (messages ?? []).ToImmutableArray();
        Diagnostics = (diagnostics ?? []).ToImmutableArray();
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
    }

    public EpubCheckEvidenceStatus Status { get; }

    public string? ToolVersion { get; }

    public int? ExitCode { get; }

    public int FatalCount { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public int UsageCount { get; }

    public ImmutableArray<EpubCheckMessageEvidence> Messages { get; }

    public ImmutableArray<EpubCorpusExecutionDiagnostic> Diagnostics { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }
}

/// <summary>Produces optional external conformance evidence without changing Flow state.</summary>
public interface IEpubCheckAdapter
{
    public Task<EpubCheckEvidence> EvaluateAsync(
        Stream epub,
        string logicalPublicationId,
        CancellationToken cancellationToken = default);
}

public static class EpubCheckDiagnosticCodes
{
    public const string ToolMissing = "EPC034";
    public const string IncompatibleVersion = "EPC035";
    public const string Timeout = "EPC036";
    public const string OutputLimitExceeded = "EPC037";
    public const string UnrecognizedOutput = "EPC038";
    public const string ToolFailure = "EPC039";
}
