using System.Text.Json;

namespace Flow.Cli;

internal sealed record CliOutputPreparation(bool IsSuccess, bool Resumed, string? ErrorResourceKey = null)
{
    public static CliOutputPreparation Ready(bool resumed = false) => new(true, resumed);

    public static CliOutputPreparation Failure(string resourceKey) => new(false, false, resourceKey);
}

internal sealed record CliOutputArtifactInspection(int TemporaryFiles, int StagingDirectories, int BackupDirectories)
{
    public int Total => TemporaryFiles + StagingDirectories + BackupDirectories;
}

internal sealed record CliOutputArtifactCleanup(
    bool IsSuccess,
    int RemovedArtifacts,
    bool RestoredBackup,
    string? ErrorResourceKey = null);

/// <summary>Applies the CLI's explicit overwrite and interrupted-run recovery policy.</summary>
internal static class CliOutputPolicy
{
    public static CliOutputArtifactInspection InspectArtifacts(string destinationPath)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output destination must have a parent directory.", nameof(destinationPath));
        if (!Directory.Exists(parent))
        {
            return new CliOutputArtifactInspection(0, 0, 0);
        }

        return new CliOutputArtifactInspection(
            Directory.EnumerateFiles(parent, $".{Path.GetFileName(destination)}.*.tmp", SearchOption.TopDirectoryOnly)
                .Count(path => IsTransactionArtifact(path, destination, "tmp")),
            FindDirectories(parent, destination, "staging").Length,
            FindDirectories(parent, destination, "backup").Length);
    }

    public static CliOutputArtifactCleanup CleanupArtifacts(string destinationPath)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output destination must have a parent directory.", nameof(destinationPath));
        if (!Directory.Exists(parent))
        {
            return new CliOutputArtifactCleanup(true, 0, false);
        }

        var temporaryFiles = Directory
            .EnumerateFiles(parent, $".{Path.GetFileName(destination)}.*.tmp", SearchOption.TopDirectoryOnly)
            .Where(path => IsTransactionArtifact(path, destination, "tmp"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var staging = FindDirectories(parent, destination, "staging");
        var backups = FindDirectories(parent, destination, "backup");
        if (backups.Length > 1)
        {
            return new CliOutputArtifactCleanup(false, 0, false, "ErrorAmbiguousReviewRecovery");
        }

        EnsurePlainFiles(temporaryFiles);
        EnsurePlainDirectories(staging.Concat(backups));
        foreach (var backup in backups)
        {
            EnsureReviewManifest(backup);
        }

        if (backups.Length == 1 && File.Exists(destination))
        {
            return new CliOutputArtifactCleanup(false, 0, false, "ErrorOutputExpectedDirectory");
        }

        foreach (var path in temporaryFiles)
        {
            File.Delete(path);
        }

        foreach (var directory in staging)
        {
            Directory.Delete(directory, recursive: true);
        }

        var restored = false;
        if (backups.Length == 1)
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(backups[0], recursive: true);
            }
            else
            {
                Directory.Move(backups[0], destination);
                restored = true;
            }
        }

        return new CliOutputArtifactCleanup(
            true,
            temporaryFiles.Length + staging.Length + backups.Length,
            restored);
    }

    public static CliOutputPreparation PrepareFile(string outputPath, bool force, bool resume)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullPath))
        {
            return CliOutputPreparation.Failure("ErrorOutputExpectedFile");
        }

        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The output path must have a parent directory.", nameof(outputPath));
        var artifacts = Directory.Exists(parent)
            ? Directory.EnumerateFiles(parent, $".{Path.GetFileName(fullPath)}.*.tmp", SearchOption.TopDirectoryOnly)
                .Where(path => IsTransactionArtifact(path, fullPath, "tmp"))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (artifacts.Length > 0 && !resume)
        {
            return CliOutputPreparation.Failure("ErrorInterruptedOutputRequiresResume");
        }

        if (resume)
        {
            EnsurePlainFiles(artifacts);
            foreach (var artifact in artifacts)
            {
                File.Delete(artifact);
            }
        }

        if (File.Exists(fullPath) && !force)
        {
            return CliOutputPreparation.Failure("ErrorOutputExistsRequiresForce");
        }

        return CliOutputPreparation.Ready(artifacts.Length > 0);
    }

    public static CliOutputPreparation PrepareReviewDirectory(string outputDirectory, bool force, bool resume)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (File.Exists(destination))
        {
            return CliOutputPreparation.Failure("ErrorOutputExpectedDirectory");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output directory must have a parent directory.", nameof(outputDirectory));
        var staging = FindDirectories(parent, destination, "staging");
        var backups = FindDirectories(parent, destination, "backup");
        var hasArtifacts = staging.Length > 0 || backups.Length > 0;
        if (hasArtifacts && !resume)
        {
            return CliOutputPreparation.Failure("ErrorInterruptedOutputRequiresResume");
        }

        if (resume)
        {
            EnsurePlainDirectories(staging.Concat(backups));
            if (backups.Length > 1)
            {
                return CliOutputPreparation.Failure("ErrorAmbiguousReviewRecovery");
            }

            foreach (var backup in backups)
            {
                EnsureReviewManifest(backup);
            }

            foreach (var directory in staging)
            {
                Directory.Delete(directory, recursive: true);
            }

            if (backups.Length == 1)
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(backups[0], recursive: true);
                }
                else
                {
                    Directory.Move(backups[0], destination);
                }
            }
        }

        if (Directory.Exists(destination) && !force)
        {
            return CliOutputPreparation.Failure("ErrorOutputExistsRequiresForce");
        }

        return CliOutputPreparation.Ready(hasArtifacts);
    }

    private static string[] FindDirectories(string parent, string destination, string suffix) =>
        Directory.Exists(parent)
            ? Directory.EnumerateDirectories(
                    parent,
                    $".{Path.GetFileName(destination)}.*.{suffix}",
                    SearchOption.TopDirectoryOnly)
                .Where(path => IsTransactionArtifact(path, destination, suffix))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

    private static bool IsTransactionArtifact(string candidate, string destination, string suffix)
    {
        var name = Path.GetFileName(candidate);
        var prefix = $".{Path.GetFileName(destination)}.";
        var ending = $".{suffix}";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(ending, StringComparison.Ordinal))
        {
            return false;
        }

        var token = name[prefix.Length..^ending.Length];
        return token.Length == 32 && Guid.TryParseExact(token, "N", out _);
    }

    private static void EnsurePlainFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("An interrupted-run artifact is an unsafe reparse point.");
            }
        }
    }

    private static void EnsurePlainDirectories(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("An interrupted-run directory is an unsafe reparse point.");
            }
        }
    }

    private static void EnsureReviewManifest(string directory)
    {
        try
        {
            var perBookPath = Path.Combine(directory, "review-manifest.json");
            var corpusPath = Path.Combine(directory, "corpus-review.json");
            var path = File.Exists(perBookPath) ? perBookPath : corpusPath;
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(path));
            var format = manifest.RootElement.GetProperty("format").GetString();
            if (format is not "flow-epub-large-review-manifest-0.1"
                and not "flow-epub-private-visual-review-0.1")
            {
                throw new IOException("An interrupted review backup has an unknown format.");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new IOException("An interrupted review backup is not a recognized Flow review directory.", exception);
        }
    }
}
