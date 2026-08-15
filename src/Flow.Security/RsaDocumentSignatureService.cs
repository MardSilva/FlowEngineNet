using System.Security.Cryptography;
using Flow.Documents;

namespace Flow.Security;

public sealed class RsaDocumentSignatureService : IDocumentSigner, IDocumentSignatureVerifier
{
    public const string Algorithm = "RSA-PSS-SHA256";
    public const int MinimumKeySize = 2048;

    private readonly IDocumentCanonicalizer _canonicalizer;

    public RsaDocumentSignatureService(IDocumentCanonicalizer canonicalizer)
    {
        ArgumentNullException.ThrowIfNull(canonicalizer);
        _canonicalizer = canonicalizer;
    }

    public DocumentSignature Sign(
        FlowDocument document,
        AsymmetricAlgorithm signingKey,
        string keyId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        if (signingKey is not RSA rsa)
        {
            throw new NotSupportedException("RSA-PSS-SHA256 requires an RSA signing key.");
        }

        EnsureMinimumKeySize(rsa);
        var canonicalBytes = _canonicalizer.Canonicalize(document);
        var signature = rsa.SignData(
            canonicalBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        return new DocumentSignature(
            Algorithm,
            keyId,
            _canonicalizer.CanonicalizationVersion,
            signature);
    }

    public SignatureVerificationResult Verify(
        FlowDocument document,
        DocumentSignature signature,
        AsymmetricAlgorithm verificationKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(verificationKey);

        if (!string.Equals(signature.Algorithm, Algorithm, StringComparison.Ordinal))
        {
            return SignatureVerificationResult.Failure(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"Signature algorithm '{signature.Algorithm}' is not supported.");
        }

        if (!string.Equals(
                signature.CanonicalizationVersion,
                _canonicalizer.CanonicalizationVersion,
                StringComparison.Ordinal))
        {
            return SignatureVerificationResult.Failure(
                SignatureVerificationStatus.UnsupportedCanonicalization,
                $"Canonicalization profile '{signature.CanonicalizationVersion}' is not supported.");
        }

        if (verificationKey is not RSA rsa || rsa.KeySize < MinimumKeySize)
        {
            return SignatureVerificationResult.Failure(
                SignatureVerificationStatus.InvalidKey,
                $"Verification requires an RSA key of at least {MinimumKeySize} bits.");
        }

        try
        {
            var canonicalBytes = _canonicalizer.Canonicalize(document);
            var isValid = rsa.VerifyData(
                canonicalBytes,
                signature.Value.AsSpan(),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            return isValid
                ? SignatureVerificationResult.Valid()
                : SignatureVerificationResult.Failure(
                    SignatureVerificationStatus.InvalidSignature,
                    "The signature does not match the canonical document bytes and verification key.");
        }
        catch (CryptographicException)
        {
            return SignatureVerificationResult.Failure(
                SignatureVerificationStatus.InvalidKey,
                "The verification key could not verify an RSA-PSS-SHA256 signature.");
        }
    }

    private static void EnsureMinimumKeySize(RSA key)
    {
        if (key.KeySize < MinimumKeySize)
        {
            throw new CryptographicException(
                $"RSA signing keys must be at least {MinimumKeySize} bits.");
        }
    }
}
