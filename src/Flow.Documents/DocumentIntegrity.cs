namespace Flow.Documents;

public sealed record DocumentIntegrity
{
    public DocumentIntegrity(string algorithm, string hash, string canonicalizationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalizationVersion);

        Algorithm = algorithm;
        Hash = hash;
        CanonicalizationVersion = canonicalizationVersion;
    }

    public string Algorithm { get; }

    public string Hash { get; }

    public string CanonicalizationVersion { get; }
}
