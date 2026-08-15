using Flow.Documents;

namespace Flow.Security;

/// <summary>Projects canonical document state into deterministic bytes.</summary>
public interface IDocumentCanonicalizer
{
    /// <summary>Gets the versioned canonicalization profile name.</summary>
    public string CanonicalizationVersion { get; }

    /// <summary>Produces canonical bytes for a document.</summary>
    /// <param name="document">The document whose canonical state is projected.</param>
    /// <returns>Deterministic bytes owned by the caller.</returns>
    public byte[] Canonicalize(FlowDocument document);
}
