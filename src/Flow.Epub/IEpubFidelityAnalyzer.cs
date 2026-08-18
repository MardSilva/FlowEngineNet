namespace Flow.Epub;

/// <summary>Analyzes EPUB source evidence against the imported semantic result.</summary>
public interface IEpubFidelityAnalyzer
{
    /// <summary>Creates a noncanonical fidelity report without modifying the imported document.</summary>
    public EpubFidelityReport Analyze(EpubImportResult importResult);

    /// <summary>Creates a report with cooperative cancellation for large semantic documents.</summary>
    public EpubFidelityReport Analyze(
        EpubImportResult importResult,
        CancellationToken cancellationToken) => Analyze(importResult);
}
