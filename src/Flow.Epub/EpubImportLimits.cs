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
        long maximumXmlCharacters = 8 * 1024 * 1024,
        long maximumImageBytes = 16 * 1024 * 1024,
        int maximumImageWidth = 20_000,
        int maximumImageHeight = 20_000,
        long maximumImagePixels = 100_000_000,
        long maximumStylesheetBytes = 2 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCompressionRatio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumXmlCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImagePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStylesheetBytes);

        MaximumEntries = maximumEntries;
        MaximumArchiveBytes = maximumArchiveBytes;
        MaximumEntryBytes = maximumEntryBytes;
        MaximumTotalUncompressedBytes = maximumTotalUncompressedBytes;
        MaximumCompressionRatio = maximumCompressionRatio;
        MaximumXmlCharacters = maximumXmlCharacters;
        MaximumImageBytes = maximumImageBytes;
        MaximumImageWidth = maximumImageWidth;
        MaximumImageHeight = maximumImageHeight;
        MaximumImagePixels = maximumImagePixels;
        MaximumStylesheetBytes = maximumStylesheetBytes;
    }

    public int MaximumEntries { get; }

    public long MaximumArchiveBytes { get; }

    public long MaximumEntryBytes { get; }

    public long MaximumTotalUncompressedBytes { get; }

    public int MaximumCompressionRatio { get; }

    public long MaximumXmlCharacters { get; }

    /// <summary>Gets the maximum bytes accepted for one imported image asset.</summary>
    public long MaximumImageBytes { get; }

    /// <summary>Gets the maximum detected raster or SVG width.</summary>
    public int MaximumImageWidth { get; }

    /// <summary>Gets the maximum detected raster or SVG height.</summary>
    public int MaximumImageHeight { get; }

    /// <summary>Gets the maximum detected width multiplied by height.</summary>
    public long MaximumImagePixels { get; }

    /// <summary>Gets the maximum UTF-8 bytes accepted for one EPUB stylesheet.</summary>
    public long MaximumStylesheetBytes { get; }
}
