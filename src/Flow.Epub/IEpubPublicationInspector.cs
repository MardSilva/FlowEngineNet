namespace Flow.Epub;

/// <summary>Inspects EPUB container and package structure without producing a Flow document.</summary>
public interface IEpubPublicationInspector
{
    /// <summary>Inspects one readable EPUB stream using bounded ZIP and XML processing.</summary>
    public Task<EpubPublicationInspection> InspectAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
