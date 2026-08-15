using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

/// <summary>Computes SHA-256 over bytes supplied by a versioned document canonicalizer.</summary>
public sealed class Sha256DocumentIntegrityService : IDocumentIntegrityService
{
    private readonly IDocumentCanonicalizer _canonicalizer;

    public Sha256DocumentIntegrityService(IDocumentCanonicalizer canonicalizer)
    {
        ArgumentNullException.ThrowIfNull(canonicalizer);
        _canonicalizer = canonicalizer;
    }

    /// <inheritdoc />
    public DocumentHash ComputeHash(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var canonicalBytes = _canonicalizer.Canonicalize(document);
        var hash = SHA256.HashData(canonicalBytes);

        return new DocumentHash(
            "SHA-256",
            Convert.ToHexString(hash),
            _canonicalizer.CanonicalizationVersion);
    }
}
