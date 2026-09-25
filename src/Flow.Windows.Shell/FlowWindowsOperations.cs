using System.Collections.Immutable;

namespace Flow.Windows.Shell;

/// <summary>Identifies a visual document operation.</summary>
public enum FlowWindowsOperationKind
{
    Inspect,
    Import,
    Validate,
}

/// <summary>Controls how a visual operation handles an existing or interrupted destination.</summary>
public enum FlowWindowsOutputPolicy
{
    RejectExisting,
    ReplaceConfirmed,
    ResumeInterrupted,
}

/// <summary>Selects generated HTML interface text without changing authored content.</summary>
public enum FlowWindowsHtmlLanguage
{
    Automatic,
    English,
    PortugueseBrazil,
    PortuguesePortugal,
}

/// <summary>Requests one local visual workflow without transferring private publication data.</summary>
public sealed record FlowWindowsOperationRequest
{
    public FlowWindowsOperationRequest(
        FlowWindowsOperationKind operation,
        string sourcePath,
        string? documentOutputPath = null,
        bool writeDiagnosticsReport = false,
        bool writeHtmlBook = false,
        FlowWindowsHtmlLanguage htmlLanguage = FlowWindowsHtmlLanguage.Automatic,
        FlowWindowsOutputPolicy outputPolicy = FlowWindowsOutputPolicy.RejectExisting)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException("The source path must be absolute.", nameof(sourcePath));
        }

        if (documentOutputPath is not null && !Path.IsPathFullyQualified(documentOutputPath))
        {
            throw new ArgumentException("The output path must be absolute.", nameof(documentOutputPath));
        }

        if (!Enum.IsDefined(htmlLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(htmlLanguage));
        }

        if (!Enum.IsDefined(outputPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(outputPolicy));
        }

        if (operation != FlowWindowsOperationKind.Import
            && (documentOutputPath is not null || writeDiagnosticsReport || writeHtmlBook))
        {
            throw new ArgumentException("Only import operations can create output files.");
        }

        Operation = operation;
        SourcePath = Path.GetFullPath(sourcePath);
        DocumentOutputPath = documentOutputPath is null ? null : Path.GetFullPath(documentOutputPath);
        WriteDiagnosticsReport = writeDiagnosticsReport;
        WriteHtmlBook = writeHtmlBook;
        HtmlLanguage = htmlLanguage;
        OutputPolicy = outputPolicy;
    }

    public FlowWindowsOperationKind Operation { get; }

    public string SourcePath { get; }

    public string? DocumentOutputPath { get; }

    public bool WriteDiagnosticsReport { get; }

    public bool WriteHtmlBook { get; }

    public FlowWindowsHtmlLanguage HtmlLanguage { get; }

    public FlowWindowsOutputPolicy OutputPolicy { get; }
}

/// <summary>Contains local publication details shown before or after an operation.</summary>
public sealed record FlowWindowsBookSummary(
    string Title,
    ImmutableArray<string> Authors,
    string? Language,
    string? EpubVersion,
    int ResourceCount,
    int SpineItemCount,
    int AssetCount,
    string? CoverMediaType,
    ImmutableArray<byte> CoverBytes);

/// <summary>Contains one result that can be presented without parsing terminal output.</summary>
public sealed record FlowWindowsOperationResult(
    FlowWindowsOperationKind Operation,
    bool Succeeded,
    FlowWindowsBookSummary? Summary,
    ImmutableArray<Flow.Application.FlowApplicationDiagnostic> Diagnostics,
    string? DocumentOutputPath = null,
    string? DiagnosticsOutputPath = null,
    string? HtmlBookOutputPath = null,
    string? DocumentHash = null);

/// <summary>Runs local document workflows for a graphical host.</summary>
public interface IFlowWindowsOperationService
{
    public Task<FlowWindowsOperationResult> ExecuteAsync(
        FlowWindowsOperationRequest request,
        IProgress<Flow.Application.FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public string SuggestDocumentOutputPath(string sourcePath, string? title = null);

    public string GetInterruptedOutputPath(string finalPath);
}
