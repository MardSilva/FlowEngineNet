using System.Collections.Immutable;

namespace Flow.Documents;

public sealed record ValidationResult
{
    public ValidationResult(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        Diagnostics = ImmutableCollections.CopyOf(diagnostics, nameof(diagnostics));
    }

    public ImmutableArray<ValidationDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity != ValidationSeverity.Error);

    public IEnumerable<ValidationDiagnostic> Errors =>
        Diagnostics.Where(static diagnostic => diagnostic.Severity == ValidationSeverity.Error);
}
