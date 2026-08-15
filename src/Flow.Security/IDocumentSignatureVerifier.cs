using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

/// <summary>Verifies experimental signatures against canonical Flow document bytes.</summary>
public interface IDocumentSignatureVerifier
{
    /// <summary>Verifies detached signature evidence with a caller-owned public key.</summary>
    /// <param name="document">The document whose canonical bytes are verified.</param>
    /// <param name="signature">The detached signature evidence.</param>
    /// <param name="verificationKey">The caller-owned public key.</param>
    /// <returns>A typed verification outcome; it does not establish key trust.</returns>
    public SignatureVerificationResult Verify(
        FlowDocument document,
        DocumentSignature signature,
        AsymmetricAlgorithm verificationKey);
}
