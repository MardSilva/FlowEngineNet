namespace Flow.Epub;

public interface IEpubImporter
{
    public Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default);
}
