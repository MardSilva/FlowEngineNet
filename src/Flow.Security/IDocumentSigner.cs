using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

public interface IDocumentSigner
{
    public DocumentSignature Sign(
        FlowDocument document,
        AsymmetricAlgorithm signingKey,
        string keyId);
}
