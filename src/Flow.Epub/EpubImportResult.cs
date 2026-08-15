using System.Collections.Immutable;
using Flow.Documents;

namespace Flow.Epub;

public sealed record EpubImportResult
{
    public EpubImportResult(FlowDocument? document, IEnumerable<EpubDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Document = document;
        Diagnostics = diagnostics.ToImmutableArray();
    }

    public FlowDocument? Document { get; }

    public ImmutableArray<EpubDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Document is not null && Diagnostics.All(
        static diagnostic => diagnostic.Severity != EpubDiagnosticSeverity.Error);
}
