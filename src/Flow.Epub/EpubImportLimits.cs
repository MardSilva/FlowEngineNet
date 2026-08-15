namespace Flow.Epub;

/// <summary>Defines resource limits used to reduce malicious archive and XML input risk.</summary>
public sealed record EpubImportLimits
{
    public EpubImportLimits(
        int maximumEntries = 2_048,
        long maximumArchiveBytes = 128 * 1024 * 1024,
        long maximumEntryBytes = 16 * 1024 * 1024,
        long maximumTotalUncompressedBytes = 256 * 1024 * 1024,
        int maximumCompressionRatio = 200,
        long maximumXmlCharacters = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCompressionRatio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumXmlCharacters);

        MaximumEntries = maximumEntries;
        MaximumArchiveBytes = maximumArchiveBytes;
        MaximumEntryBytes = maximumEntryBytes;
        MaximumTotalUncompressedBytes = maximumTotalUncompressedBytes;
        MaximumCompressionRatio = maximumCompressionRatio;
        MaximumXmlCharacters = maximumXmlCharacters;
    }

    public int MaximumEntries { get; }

    public long MaximumArchiveBytes { get; }

    public long MaximumEntryBytes { get; }

    public long MaximumTotalUncompressedBytes { get; }

    public int MaximumCompressionRatio { get; }

    public long MaximumXmlCharacters { get; }
}
