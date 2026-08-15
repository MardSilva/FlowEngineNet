using System.Collections.Immutable;

namespace Flow.Security;

/// <summary>Contains detached signature evidence over versioned canonical document bytes.</summary>
public sealed record DocumentSignature
{
    public DocumentSignature(
        string algorithm,
        string keyId,
        string canonicalizationVersion,
        ReadOnlyMemory<byte> value)
    {
        ValidateToken(algorithm, nameof(algorithm));
        ValidateKeyId(keyId);
        ValidateToken(canonicalizationVersion, nameof(canonicalizationVersion));

        if (value.IsEmpty)
        {
            throw new ArgumentException("A document signature cannot be empty.", nameof(value));
        }

        Algorithm = algorithm;
        KeyId = keyId;
        CanonicalizationVersion = canonicalizationVersion;
        Value = ImmutableArray.CreateRange(value.ToArray());
    }

    /// <summary>Gets the closed signature algorithm identifier.</summary>
    public string Algorithm { get; }

    /// <summary>Gets the opaque caller-defined key reference.</summary>
    public string KeyId { get; }

    /// <summary>Gets the canonicalization profile used before signing.</summary>
    public string CanonicalizationVersion { get; }

    /// <summary>Gets an immutable copy of the signature bytes.</summary>
    public ImmutableArray<byte> Value { get; }

    private static void ValidateToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > 128
            || value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException(
                "The value must be a trimmed, non-empty token of at most 128 characters.",
                parameterName);
        }
    }

    private static void ValidateKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (!string.Equals(keyId, keyId.Trim(), StringComparison.Ordinal)
            || keyId.Length > 256
            || keyId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A key ID must be trimmed, contain no control characters, and be at most 256 characters.",
                nameof(keyId));
        }
    }
}
