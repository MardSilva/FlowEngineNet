using System.Security.Cryptography;

namespace Flow.Epub;

/// <summary>Implements bounded, offline discovery and temporary isolation of corpus EPUBs.</summary>
public sealed class EpubCorpusDiscoveryService : IEpubCorpusDiscoveryService
{
    private readonly Func<string> createTemporaryDirectory;

    public EpubCorpusDiscoveryService()
        : this(static () => Directory.CreateTempSubdirectory("flow-epub-corpus-").FullName)
    {
    }

    internal EpubCorpusDiscoveryService(Func<string> createTemporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(createTemporaryDirectory);
        this.createTemporaryDirectory = createTemporaryDirectory;
    }

    public async Task<EpubCorpusDiscoverySession> DiscoverAsync(
        EpubCorpusManifest manifest,
        EpubCorpusDiscoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(options);

        var temporaryDirectory = createTemporaryDirectory();
        try
        {
            EnsureSafeTemporaryDirectory(temporaryDirectory, options);
            var items = new List<EpubCorpusDiscoveryItem>(manifest.Publications.Length);
            var available = new Dictionary<EpubCorpusPublicationId, string>();
            foreach (var publication in manifest.Publications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DiscoveryResult discovery;
                try
                {
                    discovery = await DiscoverPublicationAsync(publication, options, temporaryDirectory, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (IOException)
                {
                    discovery = Result(
                        publication,
                        EpubCorpusDiscoveryStatus.UnsafePath,
                        Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The candidate could not be read safely."));
                }
                catch (UnauthorizedAccessException)
                {
                    discovery = Result(
                        publication,
                        EpubCorpusDiscoveryStatus.UnsafePath,
                        Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The candidate could not be read safely."));
                }

                items.Add(discovery.Item);
                if (discovery.TemporaryPath is not null)
                {
                    available.Add(publication.Id, discovery.TemporaryPath);
                }
            }

            return new EpubCorpusDiscoverySession(new EpubCorpusDiscoveryReport(items), available, temporaryDirectory);
        }
        catch
        {
            EpubCorpusDiscoverySession.DeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }
    }

    private static async Task<DiscoveryResult> DiscoverPublicationAsync(
        EpubCorpusPublication publication,
        EpubCorpusDiscoveryOptions options,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        var licenseDiagnostic = ValidateLicensePolicy(publication, options);
        if (licenseDiagnostic is not null)
        {
            return Result(publication, EpubCorpusDiscoveryStatus.LicenseRejected, licenseDiagnostic);
        }

        if (!TryResolveSafeRelativePath(options.RepositoryRoot, publication.RelativePath, out var repositoryCandidate))
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.UnsafePath,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The catalog path cannot be resolved safely inside the repository."));
        }

        if (publication.Kind is EpubCorpusPublicationKind.ProjectFixture or EpubCorpusPublicationKind.RedistributablePublication
            && PathExistsOrIsLink(repositoryCandidate))
        {
            if (ContainsLinkOrReparsePoint(options.RepositoryRoot, repositoryCandidate))
            {
                return Result(
                    publication,
                    EpubCorpusDiscoveryStatus.UnsafePath,
                    Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The repository candidate traverses a symbolic link or reparse point."));
            }

            var repositoryResult = await VerifyAndCopyAsync(
                    publication,
                    repositoryCandidate,
                    EpubCorpusDiscoverySource.RepositoryFixture,
                    EpubCorpusMatchKind.RelativePath,
                    options,
                    temporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (repositoryResult.Item.Status == EpubCorpusDiscoveryStatus.Available
                || publication.Kind == EpubCorpusPublicationKind.ProjectFixture
                || repositoryResult.Item.Status != EpubCorpusDiscoveryStatus.HashMismatch)
            {
                return repositoryResult;
            }
        }

        if (publication.Kind == EpubCorpusPublicationKind.ProjectFixture)
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.Missing,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.Missing, "The versioned project fixture is not present.", warning: true));
        }

        return await DiscoverExternalAsync(publication, options, temporaryDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DiscoveryResult> DiscoverExternalAsync(
        EpubCorpusPublication publication,
        EpubCorpusDiscoveryOptions options,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (options.ExternalCorpusRoot is null || !Directory.Exists(options.ExternalCorpusRoot))
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.Missing,
                Diagnostic(
                    publication,
                    options.ExternalCorpusRoot is null
                        ? EpubCorpusDiscoveryDiagnosticCodes.Missing
                        : EpubCorpusDiscoveryDiagnosticCodes.ExternalDirectoryUnavailable,
                    options.ExternalCorpusRoot is null
                        ? "No external corpus directory is configured for this publication."
                        : "The configured external corpus directory is unavailable.",
                    warning: true));
        }

        var externalRoot = options.ExternalCorpusRoot;
        if (ContainsLinkOrReparsePoint(externalRoot, externalRoot))
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.UnsafePath,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The external corpus root is a symbolic link or reparse point."));
        }

        var idCandidate = Path.Combine(externalRoot, publication.Id.Value + ".epub");
        var sawHashMismatch = false;
        if (PathExistsOrIsLink(idCandidate))
        {
            if (ContainsLinkOrReparsePoint(externalRoot, idCandidate))
            {
                return Result(
                    publication,
                    EpubCorpusDiscoveryStatus.UnsafePath,
                    Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The ID-matched candidate is a symbolic link or reparse point."));
            }

            if (await MatchesExpectedBytesAsync(idCandidate, publication, options, cancellationToken).ConfigureAwait(false))
            {
                return await VerifyAndCopyAsync(
                        publication,
                        idCandidate,
                        EpubCorpusDiscoverySource.ExternalCorpus,
                        EpubCorpusMatchKind.PublicationId,
                        options,
                        temporaryDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            sawHashMismatch = true;
        }

        var search = EnumerateSafeEpubCandidates(externalRoot, options, cancellationToken);
        if (search.LimitExceeded)
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.SearchLimitExceeded,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.SearchLimitExceeded, "The external corpus search exceeded its configured file or depth limit."));
        }

        foreach (var candidate in search.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(candidate, idCandidate, PathComparison))
            {
                continue;
            }

            if (!await MatchesExpectedBytesAsync(candidate, publication, options, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            return await VerifyAndCopyAsync(
                    publication,
                    candidate,
                    EpubCorpusDiscoverySource.ExternalCorpus,
                    EpubCorpusMatchKind.Sha256,
                    options,
                    temporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (search.UnsafeEntryFound)
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.UnsafePath,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.UnsafePath, "The external corpus contains a symbolic link or reparse point that was not followed."));
        }

        return sawHashMismatch
            ? Result(
                publication,
                EpubCorpusDiscoveryStatus.HashMismatch,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.HashMismatch, "The ID-matched publication does not have the expected size and SHA-256."))
            : Result(
                publication,
                EpubCorpusDiscoveryStatus.Missing,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.Missing, "No safe external file matched the publication ID or SHA-256.", warning: true));
    }

    private static async Task<DiscoveryResult> VerifyAndCopyAsync(
        EpubCorpusPublication publication,
        string candidate,
        EpubCorpusDiscoverySource source,
        EpubCorpusMatchKind matchKind,
        EpubCorpusDiscoveryOptions options,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (!await MatchesExpectedBytesAsync(candidate, publication, options, cancellationToken).ConfigureAwait(false))
        {
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.HashMismatch,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.HashMismatch, "The discovered publication does not have the expected size and SHA-256."));
        }

        var destination = Path.Combine(temporaryDirectory, publication.Id.Value + ".epub");
        try
        {
            await using (var sourceStream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destinationStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
                await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!await MatchesExpectedBytesAsync(destination, publication, options, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(destination);
                return Result(
                    publication,
                    EpubCorpusDiscoveryStatus.HashMismatch,
                    Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.HashMismatch, "The isolated copy changed while the publication was being discovered."));
            }

            return new DiscoveryResult(
                new EpubCorpusDiscoveryItem(publication.Id, EpubCorpusDiscoveryStatus.Available, source, matchKind),
                destination);
        }
        catch (OperationCanceledException)
        {
            TryDelete(destination);
            throw;
        }
        catch (IOException)
        {
            TryDelete(destination);
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.UnsafePath,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.TemporaryCopyFailed, "The verified publication could not be isolated in the temporary workspace."));
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(destination);
            return Result(
                publication,
                EpubCorpusDiscoveryStatus.UnsafePath,
                Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.TemporaryCopyFailed, "The verified publication could not be isolated in the temporary workspace."));
        }
    }

    private static async Task<bool> MatchesExpectedBytesAsync(
        string path,
        EpubCorpusPublication publication,
        EpubCorpusDiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (length != publication.ExpectedSizeBytes || length > options.MaximumCandidateBytes)
        {
            return false;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), publication.Sha256.Value, StringComparison.Ordinal);
    }

    private static SearchResult EnumerateSafeEpubCandidates(
        string root,
        EpubCorpusDiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var directories = new Queue<(string Path, int Depth)>();
        directories.Enqueue((root, 0));
        var examined = 0;
        var unsafeEntryFound = false;

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = directories.Dequeue();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(current.Path);
            }
            catch (IOException)
            {
                unsafeEntryFound = true;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                unsafeEntryFound = true;
                continue;
            }

            foreach (var entry in entries.Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;
                if (examined > options.MaximumCandidateFiles)
                {
                    return new SearchResult([], unsafeEntryFound, LimitExceeded: true);
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    unsafeEntryFound = true;
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    unsafeEntryFound = true;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    unsafeEntryFound = true;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (current.Depth >= options.MaximumSearchDepth)
                    {
                        return new SearchResult([], unsafeEntryFound, LimitExceeded: true);
                    }

                    directories.Enqueue((entry, current.Depth + 1));
                }
                else if (string.Equals(Path.GetExtension(entry), ".epub", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(entry);
                }
            }
        }

        return new SearchResult(files, unsafeEntryFound, LimitExceeded: false);
    }

    private static EpubCorpusDiagnostic? ValidateLicensePolicy(
        EpubCorpusPublication publication,
        EpubCorpusDiscoveryOptions options)
    {
        var validClassification = publication.Kind == EpubCorpusPublicationKind.LocalNonRedistributablePublication
            ? publication.Redistribution == EpubCorpusRedistribution.Prohibited
            : publication.Redistribution == EpubCorpusRedistribution.Allowed;
        if (!validClassification || string.IsNullOrWhiteSpace(publication.License.Name) || string.IsNullOrWhiteSpace(publication.License.Evidence))
        {
            return Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.LicenseRejected, "The publication's license classification is incomplete or inconsistent.");
        }

        if (publication.Kind == EpubCorpusPublicationKind.LocalNonRedistributablePublication
            && options.ExternalCorpusRoot is not null
            && IsWithinRoot(options.RepositoryRoot, options.ExternalCorpusRoot))
        {
            return Diagnostic(publication, EpubCorpusDiscoveryDiagnosticCodes.LicenseRejected, "A non-redistributable publication cannot be discovered from inside the repository.");
        }

        return null;
    }

    private static bool TryResolveSafeRelativePath(string root, string relativePath, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains('\\')
            || relativePath.Contains(':')
            || relativePath.Any(static character => char.IsControl(character))
            || !HasValidPercentEncoding(relativePath))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(relativePath);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (Path.IsPathFullyQualified(decoded)
            || decoded.Contains('\\')
            || decoded.Contains(':')
            || decoded.Any(static character => char.IsControl(character)))
        {
            return false;
        }

        var segments = decoded.Split('/', StringSplitOptions.None);
        if (segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        result = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        return IsWithinRoot(root, result);
    }

    private static bool HasValidPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }

            index += 2;
        }

        return true;
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathFullyQualified(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool ContainsLinkOrReparsePoint(string root, string path)
    {
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var targetFullPath = Path.GetFullPath(path);
        if (!IsWithinRoot(rootFullPath, targetFullPath) && !string.Equals(rootFullPath, targetFullPath, PathComparison))
        {
            return true;
        }

        var current = rootFullPath;
        if (IsLinkOrReparsePoint(current))
        {
            return true;
        }

        var relative = Path.GetRelativePath(rootFullPath, targetFullPath);
        if (relative == ".")
        {
            return false;
        }

        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLinkOrReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || new FileInfo(path).LinkTarget is not null
                || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool PathExistsOrIsLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return true;
        }

        try
        {
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureSafeTemporaryDirectory(string temporaryDirectory, EpubCorpusDiscoveryOptions options)
    {
        var fullPath = Path.GetFullPath(temporaryDirectory);
        if (!Directory.Exists(fullPath)
            || IsWithinRoot(options.RepositoryRoot, fullPath)
            || (options.ExternalCorpusRoot is not null && IsWithinRoot(options.ExternalCorpusRoot, fullPath))
            || ContainsLinkOrReparsePoint(fullPath, fullPath))
        {
            throw new IOException("The corpus temporary workspace is not an isolated physical directory.");
        }
    }

    private static DiscoveryResult Result(
        EpubCorpusPublication publication,
        EpubCorpusDiscoveryStatus status,
        EpubCorpusDiagnostic diagnostic) =>
        new(new EpubCorpusDiscoveryItem(publication.Id, status, diagnostics: [diagnostic]), null);

    private static EpubCorpusDiagnostic Diagnostic(
        EpubCorpusPublication publication,
        string code,
        string message,
        bool warning = false) =>
        new(
            code,
            warning ? EpubCorpusDiagnosticSeverity.Warning : EpubCorpusDiagnosticSeverity.Error,
            message,
            $"publications/{publication.Id.Value}");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record DiscoveryResult(EpubCorpusDiscoveryItem Item, string? TemporaryPath);

    private sealed record SearchResult(IReadOnlyList<string> Files, bool UnsafeEntryFound, bool LimitExceeded);
}
