namespace Flow.Security;

/// <summary>Classifies the mathematical result of signature verification.</summary>
public enum SignatureVerificationStatus
{
    Valid,
    InvalidSignature,
    UnsupportedAlgorithm,
    UnsupportedCanonicalization,
    InvalidKey,
}

/// <summary>Reports signature verification without making a key-trust or identity claim.</summary>
public sealed record SignatureVerificationResult
{
    private SignatureVerificationResult(SignatureVerificationStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public SignatureVerificationStatus Status { get; }

    public string Message { get; }

    /// <summary>Gets whether the mathematical signature verification succeeded.</summary>
    public bool IsValid => Status == SignatureVerificationStatus.Valid;

    public static SignatureVerificationResult Valid() =>
        new(SignatureVerificationStatus.Valid, "The document signature is valid.");

    public static SignatureVerificationResult Failure(
        SignatureVerificationStatus status,
        string message)
    {
        if (status == SignatureVerificationStatus.Valid)
        {
            throw new ArgumentException("A failure result cannot have Valid status.", nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new SignatureVerificationResult(status, message);
    }
}
