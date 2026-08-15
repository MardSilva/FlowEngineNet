namespace Flow.Security;

public enum SignatureVerificationStatus
{
    Valid,
    InvalidSignature,
    UnsupportedAlgorithm,
    UnsupportedCanonicalization,
    InvalidKey,
}

public sealed record SignatureVerificationResult
{
    private SignatureVerificationResult(SignatureVerificationStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public SignatureVerificationStatus Status { get; }

    public string Message { get; }

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
