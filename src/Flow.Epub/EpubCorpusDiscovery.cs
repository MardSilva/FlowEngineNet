using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Classifies the outcome of locating and verifying one corpus publication.</summary>
public enum EpubCorpusDiscoveryStatus
{
    Available,
    Missing,
    HashMismatch,
    LicenseRejected,
    UnsafePath,
    SearchLimitExceeded,
}

/// <summary>Identifies the logical source from which a corpus publication was selected.</summary>
public enum EpubCorpusDiscoverySource
{
    RepositoryFixture,
    ExternalCorpus,
}

/// <summary>Identifies how a physical input was associated with a stable catalog entry.</summary>
public enum EpubCorpusMatchKind
{
    RelativePath,
    PublicationId,
    Sha256,
}

/// <summary>Contains path-free, deterministic discovery evidence for one publication.</summary>
public sealed record EpubCorpusDiscoveryItem
{
    public EpubCorpusDiscoveryItem(
        EpubCorpusPublicationId id,
        EpubCorpusDiscoveryStatus status,
        EpubCorpusDiscoverySource? source = null,
        EpubCorpusMatchKind? matchKind = null,
        IEnumerable<EpubCorpusDiagnostic>? diagnostics = null)
    {
        Id = id;
        Status = status;
        Source = source;
        MatchKind = matchKind;
        Diagnostics = (diagnostics ?? [])
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubCorpusDiscoveryStatus Status { get; }

    public EpubCorpusDiscoverySource? Source { get; }

    public EpubCorpusMatchKind? MatchKind { get; }

    public ImmutableArray<EpubCorpusDiagnostic> Diagnostics { get; }
}

/// <summary>Contains deterministic, path-free discovery evidence for a corpus manifest.</summary>
public sealed record EpubCorpusDiscoveryReport
{
    public const string CurrentFormat = "flow-epub-corpus-discovery-0.1";

    public EpubCorpusDiscoveryReport(IEnumerable<EpubCorpusDiscoveryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public ImmutableArray<EpubCorpusDiscoveryItem> Items { get; }

    public bool AllAvailable => Items.All(static item => item.Status == EpubCorpusDiscoveryStatus.Available);
}

/// <summary>Configures bounded, offline discovery of corpus publications.</summary>
public sealed record EpubCorpusDiscoveryOptions
{
    public const string ExternalCorpusEnvironmentVariable = "FLOW_EPUB_CORPUS_PATH";

    public EpubCorpusDiscoveryOptions(
        string repositoryRoot,
        string? externalCorpusRoot = null,
        int maximumCandidateFiles = 4_096,
        int maximumSearchDepth = 16,
        long maximumCandidateBytes = 128 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Path.IsPathFullyQualified(repositoryRoot))
        {
            throw new ArgumentException("The repository root must be an absolute path.", nameof(repositoryRoot));
        }

        if (!string.IsNullOrWhiteSpace(externalCorpusRoot) && !Path.IsPathFullyQualified(externalCorpusRoot))
        {
            throw new ArgumentException("The external corpus root must be an absolute path when supplied.", nameof(externalCorpusRoot));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSearchDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateBytes);

        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        ExternalCorpusRoot = string.IsNullOrWhiteSpace(externalCorpusRoot)
            ? null
            : Path.GetFullPath(externalCorpusRoot);
        MaximumCandidateFiles = maximumCandidateFiles;
        MaximumSearchDepth = maximumSearchDepth;
        MaximumCandidateBytes = maximumCandidateBytes;
    }

    public string RepositoryRoot { get; }

    public string? ExternalCorpusRoot { get; }

    public int MaximumCandidateFiles { get; }

    public int MaximumSearchDepth { get; }

    public long MaximumCandidateBytes { get; }

    public static EpubCorpusDiscoveryOptions FromEnvironment(string repositoryRoot) => new(
        repositoryRoot,
        Environment.GetEnvironmentVariable(ExternalCorpusEnvironmentVariable));
}

/// <summary>Owns verified temporary copies until corpus processing finishes.</summary>
public sealed class EpubCorpusDiscoverySession : IAsyncDisposable, IDisposable
{
    private readonly Dictionary<EpubCorpusPublicationId, string> availableFiles;
    private readonly string temporaryDirectory;
    private bool disposed;

    internal EpubCorpusDiscoverySession(
        EpubCorpusDiscoveryReport report,
        Dictionary<EpubCorpusPublicationId, string> availableFiles,
        string temporaryDirectory)
    {
        Report = report;
        this.availableFiles = availableFiles;
        this.temporaryDirectory = temporaryDirectory;
    }

    public EpubCorpusDiscoveryReport Report { get; }

    public Stream OpenRead(EpubCorpusPublicationId id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!availableFiles.TryGetValue(id, out var path))
        {
            throw new KeyNotFoundException($"Corpus publication '{id}' is not available in this discovery session.");
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        DeleteTemporaryDirectory(temporaryDirectory);
        availableFiles.Clear();
        disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal string TemporaryDirectory => temporaryDirectory;

    internal static void DeleteTemporaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}

/// <summary>Finds and verifies cataloged EPUB files without importing their content.</summary>
public interface IEpubCorpusDiscoveryService
{
    public Task<EpubCorpusDiscoverySession> DiscoverAsync(
        EpubCorpusManifest manifest,
        EpubCorpusDiscoveryOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Defines stable diagnostic codes produced by EPUB corpus discovery.</summary>
public static class EpubCorpusDiscoveryDiagnosticCodes
{
    public const string Missing = "EPC013";
    public const string HashMismatch = "EPC014";
    public const string LicenseRejected = "EPC015";
    public const string UnsafePath = "EPC016";
    public const string ExternalDirectoryUnavailable = "EPC017";
    public const string TemporaryCopyFailed = "EPC018";
    public const string SearchLimitExceeded = "EPC019";
}
