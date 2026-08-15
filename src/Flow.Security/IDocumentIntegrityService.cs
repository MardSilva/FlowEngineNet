using Flow.Documents;

namespace Flow.Security;

/// <summary>Computes integrity evidence over canonical document bytes.</summary>
public interface IDocumentIntegrityService
{
    /// <summary>Computes a versioned document hash.</summary>
    /// <param name="document">The document to hash.</param>
    /// <returns>The algorithm, digest, and canonicalization profile.</returns>
    public DocumentHash ComputeHash(FlowDocument document);
}
