namespace Flow.Epub;

/// <summary>Inspects EPUB container and package structure without producing a Flow document.</summary>
public interface IEpubPublicationInspector
{
    /// <summary>Inspects one readable EPUB stream using bounded ZIP and XML processing.</summary>
    public Task<EpubPublicationInspection> InspectAsync(
        Stream source,
        CancellationToken cancellationToken = default);

    /// <summary>Inspects an EPUB while reporting isolated, noncanonical progress observations.</summary>
    public Task<EpubPublicationInspection> InspectAsync(
        Stream source,
        IProgress<EpubImportProgress>? progress,
        CancellationToken cancellationToken = default) => InspectAsync(source, cancellationToken);
}
