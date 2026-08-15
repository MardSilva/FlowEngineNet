using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

public interface IDocumentSignatureVerifier
{
    public SignatureVerificationResult Verify(
        FlowDocument document,
        DocumentSignature signature,
        AsymmetricAlgorithm verificationKey);
}
