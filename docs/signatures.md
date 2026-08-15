# Experimental document signatures 0.1

Flow `0.1.0-rc.1` includes a deliberately narrow local proof of concept for signing canonical document bytes. It demonstrates cryptographic integrity with caller-managed keys; it does not define identity, trust, certificates, or a public-key infrastructure.

## Signed bytes

`RsaDocumentSignatureService` calls the configured `IDocumentCanonicalizer` and passes exactly the returned bytes to the standard .NET `RSA.SignData` API. For the current implementation, those bytes use `flow-c14n-0.1`.

The service does not sign:

- formatted `.flow.json` bytes;
- a `DocumentHash` string;
- presentation, reader preferences, layout, or renderer output;
- the `DocumentSignature` itself.

Because presentation is excluded from `flow-c14n-0.1`, the same signature remains valid when only theme or other non-canonical presentation changes. A semantic content or canonical metadata change invalidates it.

## Algorithm profile

The only implemented identifier is:

```text
RSA-PSS-SHA256
```

It maps directly to standard .NET APIs:

- `RSA.SignData` / `RSA.VerifyData`;
- `HashAlgorithmName.SHA256`;
- `RSASignaturePadding.Pss`;
- RSA keys of at least 2048 bits.

No Flow-specific cryptographic primitive is introduced. RSA-PSS is probabilistic, so signing the same canonical bytes twice is not expected to produce identical signature bytes.

## Signature value

`DocumentSignature` contains only:

| Field | Meaning |
| --- | --- |
| `Algorithm` | Closed algorithm identifier used for dispatch |
| `KeyId` | Opaque caller-provided reference to a key |
| `CanonicalizationVersion` | Exact canonical profile used before signing |
| `Value` | Immutable signature bytes |

`KeyId` does not prove who owns a key. It is not a certificate, issuer, account, URL resolver, or trust assertion.

Signatures are not yet serialized inside `.flow.json`. Embedding and transport require a separate versioned design and must avoid recursive inclusion in canonical bytes.

## Verification results

Verification returns a typed `SignatureVerificationResult` with one of these statuses:

- `Valid`;
- `InvalidSignature`;
- `UnsupportedAlgorithm`;
- `UnsupportedCanonicalization`;
- `InvalidKey`.

A `Valid` result means only that the supplied public key verifies the signature over the current canonical bytes. The caller remains responsible for obtaining the correct public key and deciding whether it is trusted.

## Local example

```csharp
using System.Security.Cryptography;
using Flow.Security;

using var privateKey = RSA.Create(2048);
using var publicKey = RSA.Create();
publicKey.ImportParameters(privateKey.ExportParameters(false));

var signatures = new RsaDocumentSignatureService(
    new FlowDocumentCanonicalizer());

DocumentSignature signature = signatures.Sign(
    document,
    privateKey,
    keyId: "local-demo-key");

SignatureVerificationResult result = signatures.Verify(
    document,
    signature,
    publicKey);
```

The service never owns or disposes the supplied keys. Private-key storage, rotation, access control, public-key distribution, revocation, and trust policy remain outside Flow.

The implementation follows the standard [.NET RSA signing API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.signdata?view=net-10.0) and [.NET RSA verification API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.verifydata?view=net-10.0).

No package, key profile, or signature envelope is published in Flow 0.1. See [known limitations](known-limitations.md).
