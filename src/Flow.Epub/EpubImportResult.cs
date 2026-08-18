using System.Collections.Immutable;
using Flow.Documents;

namespace Flow.Epub;

/// <summary>Contains an optional document, noncanonical source/processing evidence, and every import diagnostic.</summary>
public sealed record EpubImportResult
{
    public EpubImportResult(
        FlowDocument? document,
        IEnumerable<EpubDiagnostic> diagnostics,
        EpubMetadataReport? metadataReport = null,
        EpubPackageProcessingReport? processingReport = null,
        EpubSourceMap? sourceMap = null)
        : this(document, diagnostics, metadataReport, processingReport, sourceMap, null)
    {
    }

    internal EpubImportResult(
        FlowDocument? document,
        IEnumerable<EpubDiagnostic> diagnostics,
        EpubMetadataReport? metadataReport,
        EpubPackageProcessingReport? processingReport,
        EpubSourceMap? sourceMap,
        EpubFidelitySourceSnapshot? fidelitySource)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Document = document;
        Diagnostics = diagnostics.ToImmutableArray();
        MetadataReport = metadataReport;
        ProcessingReport = processingReport;
        SourceMap = sourceMap;
        FidelitySource = fidelitySource;
    }

    /// <summary>Gets the imported document, including a recoverable partial result when available.</summary>
    public FlowDocument? Document { get; }

    /// <summary>Gets immutable diagnostics in discovery order.</summary>
    public ImmutableArray<EpubDiagnostic> Diagnostics { get; }

    /// <summary>Gets noncanonical typed metadata retained from the OPF source.</summary>
    public EpubMetadataReport? MetadataReport { get; }

    /// <summary>Gets typed evidence for manifest resolution and every declared spine position.</summary>
    public EpubPackageProcessingReport? ProcessingReport { get; }

    /// <summary>Gets noncanonical traceability from EPUB resources and fragments to semantic node IDs.</summary>
    public EpubSourceMap? SourceMap { get; }

    internal EpubFidelitySourceSnapshot? FidelitySource { get; }

    /// <summary>Gets whether a document was produced without error diagnostics.</summary>
    public bool IsSuccess => Document is not null && Diagnostics.All(
        static diagnostic => diagnostic.Severity != EpubDiagnosticSeverity.Error);
}
