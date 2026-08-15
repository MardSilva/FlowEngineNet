using System.Security.Cryptography;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Security.Tests;

public sealed class DocumentSignatureTests
{
    private readonly FlowDocumentCanonicalizer _canonicalizer = new();

    [Fact]
    public void SignAndVerify_WithMatchingDocumentAndPublicKey_IsValid()
    {
        using var privateKey = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        using var publicKey = CreatePublicKey(privateKey);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var document = CreateDocument("Signed content");

        var signature = service.Sign(document, privateKey, "local-test-key");
        var result = service.Verify(document, signature, publicKey);

        Assert.True(result.IsValid);
        Assert.Equal(SignatureVerificationStatus.Valid, result.Status);
        Assert.Equal(RsaDocumentSignatureService.Algorithm, signature.Algorithm);
        Assert.Equal("local-test-key", signature.KeyId);
        Assert.Equal(FlowDocumentCanonicalizer.Version, signature.CanonicalizationVersion);
        Assert.NotEmpty(signature.Value);
    }

    [Fact]
    public void Verify_WhenCanonicalContentChanges_IsInvalid()
    {
        using var privateKey = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        using var publicKey = CreatePublicKey(privateKey);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var signature = service.Sign(CreateDocument("Original content"), privateKey, "content-key");

        var result = service.Verify(CreateDocument("Altered content"), signature, publicKey);

        Assert.False(result.IsValid);
        Assert.Equal(SignatureVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_WithDifferentKey_IsInvalid()
    {
        using var signingKey = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        using var wrongKey = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var document = CreateDocument("Canonical content");
        var signature = service.Sign(document, signingKey, "signing-key");

        var result = service.Verify(document, signature, wrongKey);

        Assert.False(result.IsValid);
        Assert.Equal(SignatureVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_WithUnknownAlgorithm_ReturnsUnsupportedAlgorithm()
    {
        using var key = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var document = CreateDocument("Canonical content");
        var validSignature = service.Sign(document, key, "algorithm-key");
        var unknownSignature = new DocumentSignature(
            "UNKNOWN-SIGNATURE-ALGORITHM",
            validSignature.KeyId,
            validSignature.CanonicalizationVersion,
            validSignature.Value.ToArray());

        var result = service.Verify(document, unknownSignature, key);

        Assert.False(result.IsValid);
        Assert.Equal(SignatureVerificationStatus.UnsupportedAlgorithm, result.Status);
        Assert.Contains("not supported", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_SignsExactlyTheCanonicalBytes()
    {
        using var key = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var document = CreateDocument("Canonical bytes only");

        var signature = service.Sign(document, key, "canonical-key");

        Assert.True(
            key.VerifyData(
                _canonicalizer.Canonicalize(document),
                signature.Value.AsSpan(),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
    }

    [Fact]
    public void Verify_WhenOnlyPresentationChanges_RemainsValid()
    {
        using var privateKey = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        using var publicKey = CreatePublicKey(privateKey);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var signed = CreateDocument(
            "Same canonical content",
            new DocumentPresentation(theme: ReadingTheme.Light));
        var differentlyPresented = CreateDocument(
            "Same canonical content",
            new DocumentPresentation(theme: ReadingTheme.Dark));
        var signature = service.Sign(signed, privateKey, "presentation-key");

        var result = service.Verify(differentlyPresented, signature, publicKey);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Verify_WithUnknownCanonicalization_ReturnsUnsupportedCanonicalization()
    {
        using var key = RSA.Create(RsaDocumentSignatureService.MinimumKeySize);
        var service = new RsaDocumentSignatureService(_canonicalizer);
        var document = CreateDocument("Canonical content");
        var validSignature = service.Sign(document, key, "profile-key");
        var unknownProfile = new DocumentSignature(
            validSignature.Algorithm,
            validSignature.KeyId,
            "flow-c14n-unknown",
            validSignature.Value.ToArray());

        var result = service.Verify(document, unknownProfile, key);

        Assert.False(result.IsValid);
        Assert.Equal(SignatureVerificationStatus.UnsupportedCanonicalization, result.Status);
    }

    [Fact]
    public void DocumentSignature_CopiesSignatureBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };

        var signature = new DocumentSignature(
            RsaDocumentSignatureService.Algorithm,
            "immutable-key",
            FlowDocumentCanonicalizer.Version,
            bytes);
        bytes[0] = 9;

        Assert.True(signature.Value.AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }));
    }

    private static RSA CreatePublicKey(RSA privateKey)
    {
        var publicKey = RSA.Create();
        publicKey.ImportParameters(privateKey.ExportParameters(includePrivateParameters: false));
        return publicKey;
    }

    private static FlowDocument CreateDocument(
        string content,
        DocumentPresentation? presentation = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:signature:test"), version: "1"),
            new DocumentMetadata("Signature test", language: "en"),
            new DocumentContent(
            [
                new Paragraph(new NodeId("signed-paragraph"), [new Text(content)]),
            ]),
            presentation: presentation);
}
