namespace Flow.Documents;

public interface IFlowDocumentSerializer
{
    public Task SerializeAsync(
        FlowDocument document,
        Stream destination,
        CancellationToken cancellationToken = default);

    public Task<FlowDocument> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
