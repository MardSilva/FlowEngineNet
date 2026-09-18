using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Flow.Epub;
using Flow.Rendering.Html;

namespace Flow.Epub.Corpus;

/// <summary>Builds one transactional assisted-review directory for a qualified private corpus.</summary>
public sealed class EpubPrivateVisualReviewService : IEpubPrivateVisualReviewService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IEpubLargePublicationReviewPackageGenerator packageGenerator;

    public EpubPrivateVisualReviewService(IEpubLargePublicationReviewPackageGenerator? packageGenerator = null)
    {
        this.packageGenerator = packageGenerator ?? new EpubLargePublicationReviewPackageGenerator();
    }

    public async Task<EpubPrivateVisualReviewResult> GenerateAsync(
        EpubPrivateQualificationReport qualification,
        EpubCorpusSha256 qualificationReportSha256,
        EpubPrivateVisualReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(qualification);
        ArgumentNullException.ThrowIfNull(options);
        ValidatePaths(options);

        var candidatesByHash = await DiscoverCandidatesAsync(options, cancellationToken).ConfigureAwait(false);
        var parent = Path.GetDirectoryName(options.OutputDirectory)
            ?? throw new ArgumentException("The review output directory must have a parent.", nameof(options));
        Directory.CreateDirectory(parent);
        EnsurePlainPath(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(options.OutputDirectory)}.{Guid.NewGuid():N}.staging");
        Directory.CreateDirectory(staging);
        try
        {
            var items = new List<EpubPrivateVisualReviewItem>(qualification.Publications.Length);
            foreach (var publication in qualification.Publications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!publication.Eligible
                    || publication.Status != EpubPrivateQualificationStatus.Passed
                    || !publication.StableAcrossRepeatedRuns
                    || publication.SourceSha256 is null)
                {
                    items.Add(new EpubPrivateVisualReviewItem(
                        publication.Id,
                        EpubPrivateVisualReviewStatus.SkippedQualification,
                        publication.SourceSha256,
                        null,
                        null,
                        failureCode: "qualification-not-reviewable"));
                    continue;
                }

                if (!candidatesByHash.TryGetValue(publication.SourceSha256.Value.Value, out var sourcePath))
                {
                    items.Add(new EpubPrivateVisualReviewItem(
                        publication.Id,
                        EpubPrivateVisualReviewStatus.MissingSource,
                        publication.SourceSha256,
                        null,
                        null,
                        failureCode: "source-not-found"));
                    continue;
                }

                try
                {
                    var relativeDirectory = publication.Id.Value;
                    var candidate = new EpubLargePublicationCandidate(
                        publication.Id,
                        sourcePath,
                        options.LegalUseDeclared,
                        options.DrmFreeDeclared);
                    var result = await packageGenerator.GenerateAsync(
                            candidate,
                            new EpubLargePublicationReviewOptions(
                                publication.Id,
                                publication.SourceSha256.Value,
                                Path.Combine(staging, relativeDirectory),
                                options.RepositoryRoot,
                                options.UiLanguage),
                            cancellationToken)
                        .ConfigureAwait(false);
                    items.Add(new EpubPrivateVisualReviewItem(
                        publication.Id,
                        EpubPrivateVisualReviewStatus.Generated,
                        result.SourceEpubSha256,
                        result.CanonicalDocumentSha256,
                        relativeDirectory,
                        result.Samples.Select(static item => item.Position.ToString().ToLowerInvariant()),
                        result.Targets.Select(static item => item.Category)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (IsRecoverableCandidateFailure(exception))
                {
                    items.Add(new EpubPrivateVisualReviewItem(
                        publication.Id,
                        EpubPrivateVisualReviewStatus.Failed,
                        publication.SourceSha256,
                        null,
                        null,
                        failureCode: "package-generation-failed"));
                }
            }

            var report = new EpubPrivateVisualReviewReport(qualificationReportSha256, items);
            await File.WriteAllBytesAsync(
                    Path.Combine(staging, "corpus-review.json"),
                    EpubPrivateVisualReviewReportJsonSerializer.Serialize(report),
                    cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(
                    Path.Combine(staging, "index.html"),
                    CreateIndex(report, options.UiLanguage),
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            CommitDirectory(staging, options.OutputDirectory);
            return new EpubPrivateVisualReviewResult(options.OutputDirectory, report);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static async Task<Dictionary<string, string>> DiscoverCandidatesAsync(
        EpubPrivateVisualReviewOptions options,
        CancellationToken cancellationToken)
    {
        var files = EnumerateSafeEpubFiles(options, cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length > options.MaximumCandidateBytes)
            {
                continue;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            result.TryAdd(hash, path);
        }

        return result;
    }

    private static bool IsRecoverableCandidateFailure(Exception exception) =>
        exception is not OperationCanceledException
        and not OutOfMemoryException
        and not StackOverflowException
        and not AccessViolationException;

    private static string[] EnumerateSafeEpubFiles(
        EpubPrivateVisualReviewOptions options,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((options.SourceDirectory, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var directory = new DirectoryInfo(current.Path);
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The private review source contains a symbolic link or reparse point.");
            }

            foreach (var entry in directory.EnumerateFileSystemInfos().OrderBy(static item => item.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException("The private review source contains a symbolic link or reparse point.");
                }

                if (entry is DirectoryInfo child)
                {
                    if (current.Depth < options.MaximumRecursionDepth)
                    {
                        pending.Push((child.FullName, current.Depth + 1));
                    }

                    continue;
                }

                if (entry is FileInfo file
                    && string.Equals(file.Extension, ".epub", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file.FullName);
                    if (files.Count > options.MaximumCandidateFiles)
                    {
                        throw new IOException("The private review source exceeded the configured EPUB file limit.");
                    }
                }
            }
        }

        return files.Order(StringComparer.Ordinal).ToArray();
    }

    private static byte[] CreateIndex(EpubPrivateVisualReviewReport report, HtmlBookUiLanguage language)
    {
        var portuguese = language == HtmlBookUiLanguage.PortugueseBrazil;
        var htmlLanguage = portuguese ? "pt-BR" : "en";
        var title = portuguese ? "Revisão visual assistida do corpus" : "Assisted corpus visual review";
        var instructions = portuguese
            ? "Abra cada pacote, compare as amostras mobile e desktop e registre as decisões no checklist do candidato."
            : "Open each package, compare its mobile and desktop samples, and record decisions in the candidate checklist.";
        var statusLabel = portuguese ? "Situação" : "Status";
        var openLabel = portuguese ? "Abrir revisão" : "Open review";
        var builder = new StringBuilder();
        builder.Append("<!doctype html>\n<html lang=\"").Append(htmlLanguage)
            .Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(HtmlEncoder.Default.Encode(title)).Append("</title></head><body>\n<h1>")
            .Append(HtmlEncoder.Default.Encode(title)).Append("</h1><p>")
            .Append(HtmlEncoder.Default.Encode(instructions)).Append("</p><ul>\n");
        foreach (var item in report.Candidates)
        {
            builder.Append("<li><code>").Append(HtmlEncoder.Default.Encode(item.Id.Value)).Append("</code> — ")
                .Append(HtmlEncoder.Default.Encode(statusLabel)).Append(": ")
                .Append(HtmlEncoder.Default.Encode(StatusText(item.Status, portuguese)));
            if (item.RelativeDirectory is not null)
            {
                builder.Append(" — <a href=\"").Append(HtmlEncoder.Default.Encode(item.RelativeDirectory))
                    .Append("/review.html\">").Append(HtmlEncoder.Default.Encode(openLabel)).Append("</a>");
            }

            builder.Append("</li>\n");
        }

        builder.Append("</ul></body></html>\n");
        return Utf8WithoutBom.GetBytes(builder.ToString());
    }

    private static string StatusText(EpubPrivateVisualReviewStatus status, bool portuguese) => (status, portuguese) switch
    {
        (EpubPrivateVisualReviewStatus.Generated, true) => "gerado",
        (EpubPrivateVisualReviewStatus.MissingSource, true) => "origem ausente",
        (EpubPrivateVisualReviewStatus.SkippedQualification, true) => "ignorado pela qualificação",
        (EpubPrivateVisualReviewStatus.Failed, true) => "falha",
        (EpubPrivateVisualReviewStatus.Generated, false) => "generated",
        (EpubPrivateVisualReviewStatus.MissingSource, false) => "missing source",
        (EpubPrivateVisualReviewStatus.SkippedQualification, false) => "skipped by qualification",
        (EpubPrivateVisualReviewStatus.Failed, false) => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static void ValidatePaths(EpubPrivateVisualReviewOptions options)
    {
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException("The private review source directory does not exist.");
        }

        if (IsInside(options.RepositoryRoot, options.SourceDirectory)
            || IsInside(options.RepositoryRoot, options.OutputDirectory))
        {
            throw new ArgumentException("Private sources and review artifacts must remain outside the repository.");
        }

        if (IsInside(options.SourceDirectory, options.OutputDirectory)
            || IsInside(options.OutputDirectory, options.SourceDirectory))
        {
            throw new ArgumentException("The review output and EPUB source directories must not contain each other.");
        }

        EnsurePlainPath(options.SourceDirectory);
        if (Directory.Exists(options.OutputDirectory))
        {
            EnsurePlainPath(options.OutputDirectory);
            var reportPath = Path.Combine(options.OutputDirectory, "corpus-review.json");
            if (!IsRecognizedReport(reportPath))
            {
                throw new IOException("An existing destination is not a recognized Flow corpus review directory.");
            }
        }
    }

    private static bool IsRecognizedReport(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && document.RootElement.TryGetProperty("format", out var format)
                   && format.GetString() == EpubPrivateVisualReviewReport.CurrentFormat;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static void EnsurePlainPath(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("A private review path contains a symbolic link or reparse point.");
            }
        }
    }

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedRoot, normalizedPath, comparison)
               || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void CommitDirectory(string staging, string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.backup");
        var hadDestination = Directory.Exists(destination);
        try
        {
            if (hadDestination)
            {
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
            if (hadDestination)
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }
    }
}
