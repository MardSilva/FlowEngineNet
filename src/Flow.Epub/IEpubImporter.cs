namespace Flow.Epub;

/// <summary>Imports a security-bounded subset of EPUB into the Flow semantic model.</summary>
public interface IEpubImporter
{
    /// <summary>Imports an EPUB stream and reports all detected errors and fidelity limitations.</summary>
    /// <param name="source">A readable EPUB ZIP stream owned by the caller.</param>
    /// <param name="cancellationToken">A token that can cancel asynchronous I/O.</param>
    /// <returns>A result containing an optional document, typed source and processing reports, and diagnostics.</returns>
    public Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>Imports an EPUB while reporting isolated, noncanonical progress observations.</summary>
    public Task<EpubImportResult> ImportAsync(
        Stream source,
        IProgress<EpubImportProgress>? progress,
        CancellationToken cancellationToken = default) => ImportAsync(source, cancellationToken);
}
