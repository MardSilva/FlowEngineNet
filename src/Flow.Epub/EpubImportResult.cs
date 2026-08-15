using System.Collections.Immutable;
using Flow.Documents;

namespace Flow.Epub;

/// <summary>Contains an optional imported document and every diagnostic produced during import.</summary>
public sealed record EpubImportResult
{
    public EpubImportResult(FlowDocument? document, IEnumerable<EpubDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Document = document;
        Diagnostics = diagnostics.ToImmutableArray();
    }

    /// <summary>Gets the imported document, including a recoverable partial result when available.</summary>
    public FlowDocument? Document { get; }

    /// <summary>Gets immutable diagnostics in discovery order.</summary>
    public ImmutableArray<EpubDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether a document was produced without error diagnostics.</summary>
    public bool IsSuccess => Document is not null && Diagnostics.All(
        static diagnostic => diagnostic.Severity != EpubDiagnosticSeverity.Error);
}
