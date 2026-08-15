using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

/// <summary>Creates experimental signatures over canonical Flow document bytes.</summary>
public interface IDocumentSigner
{
    /// <summary>Signs a document using a caller-owned asymmetric key.</summary>
    /// <param name="document">The document whose canonical bytes are signed.</param>
    /// <param name="signingKey">The caller-owned private key.</param>
    /// <param name="keyId">An opaque caller-defined key reference.</param>
    /// <returns>The detached signature evidence.</returns>
    public DocumentSignature Sign(
        FlowDocument document,
        AsymmetricAlgorithm signingKey,
        string keyId);
}
