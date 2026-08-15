namespace Flow.Documents;

/// <summary>Serializes and deserializes the experimental Flow document interchange representation.</summary>
public interface IFlowDocumentSerializer
{
    /// <summary>Writes a document to a destination stream.</summary>
    /// <param name="document">The immutable document to serialize.</param>
    /// <param name="destination">A writable destination stream owned by the caller.</param>
    /// <param name="cancellationToken">A token that can cancel asynchronous I/O.</param>
    public Task SerializeAsync(
        FlowDocument document,
        Stream destination,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a document from a source stream.</summary>
    /// <param name="source">A readable source stream owned by the caller.</param>
    /// <param name="cancellationToken">A token that can cancel asynchronous I/O.</param>
    /// <returns>The deserialized immutable document.</returns>
    public Task<FlowDocument> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
