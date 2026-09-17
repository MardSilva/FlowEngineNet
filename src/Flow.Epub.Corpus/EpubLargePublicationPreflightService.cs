using System.Security.Cryptography;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Performs bounded structural preflight without importing or persisting publication content.</summary>
public sealed class EpubLargePublicationPreflightService : IEpubLargePublicationPreflightService
{
    private const string XhtmlMediaType = "application/xhtml+xml";
    private const string CssMediaType = "text/css";
    private const string SvgMediaType = "image/svg+xml";
    private const string NcxMediaType = "application/x-dtbncx+xml";
    private readonly IEpubPublicationInspector inspector;
    private readonly EpubImportLimits limits;

    public EpubLargePublicationPreflightService(
        IEpubPublicationInspector? inspector = null,
        EpubImportLimits? limits = null)
    {
        this.limits = limits ?? new EpubImportLimits();
        this.inspector = inspector ?? new EpubPublicationInspector(this.limits);
    }

    public async Task<EpubLargePublicationPreflightReport> EvaluateAsync(
        IEnumerable<EpubLargePublicationCandidate> candidates,
        string repositoryRoot,
        EpubLargePublicationPreflightCriteria? criteria = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        cancellationToken.ThrowIfCancellationRequested();
        criteria ??= new EpubLargePublicationPreflightCriteria();
        var materialized = candidates.ToArray();
        if (materialized.Any(static item => item is null))
        {
            throw new ArgumentException("Preflight candidates cannot contain null values.", nameof(candidates));
        }

        var duplicate = materialized.GroupBy(static item => item.Id).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Candidate ID '{duplicate.Key}' occurs more than once.", nameof(candidates));
        }

        var normalizedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var results = new List<EpubLargePublicationCandidateResult>(materialized.Length);
        foreach (var candidate in materialized.OrderBy(static item => item.Id.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await EvaluateCandidateAsync(
                candidate,
                normalizedRepositoryRoot,
                criteria,
                cancellationToken).ConfigureAwait(false));
        }

        var selected = results
            .Where(static item => item.Status == EpubLargePublicationCandidateStatus.Suitable)
            .OrderByDescending(static item => FeatureScore(item.Features))
            .ThenByDescending(static item => item.Resources.PresentClassCount)
            .ThenByDescending(static item => item.Resources.XhtmlBytes)
            .ThenByDescending(static item => item.LinearSpineItemCount)
            .ThenByDescending(static item => item.Resources.Xhtml)
            .ThenByDescending(static item => item.UncompressedBytes)
            .ThenBy(static item => item.Sha256?.Value, StringComparer.Ordinal)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        var reportDiagnostics = selected is null
            ? new[] { Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.NoSuitableCandidate, EpubLargePublicationPreflightSeverity.Warning) }
            : [];
        return new EpubLargePublicationPreflightReport(
            selected is null ? EpubLargePublicationSelectionStatus.Inconclusive : EpubLargePublicationSelectionStatus.Selected,
            selected?.Id,
            criteria,
            results,
            reportDiagnostics);
    }

    private async Task<EpubLargePublicationCandidateResult> EvaluateCandidateAsync(
        EpubLargePublicationCandidate candidate,
        string repositoryRoot,
        EpubLargePublicationPreflightCriteria criteria,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<EpubLargePublicationPreflightDiagnostic>();
        if (!candidate.LegalUseDeclared)
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.LegalUseNotDeclared, EpubLargePublicationPreflightSeverity.Error));
        }

        if (!candidate.DrmFreeDeclared)
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.DrmFreeNotDeclared, EpubLargePublicationPreflightSeverity.Error));
        }

        if (IsInside(repositoryRoot, candidate.Path))
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.CandidateInsideRepository, EpubLargePublicationPreflightSeverity.Error));
        }

        var file = new FileInfo(candidate.Path);
        if (!file.Exists || !string.Equals(file.Extension, ".epub", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.CandidateUnavailable, EpubLargePublicationPreflightSeverity.Error));
        }
        else if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.CandidatePathUnsafe, EpubLargePublicationPreflightSeverity.Error));
        }
        else if (file.Length > limits.MaximumArchiveBytes)
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.ArchiveLimitExceeded, EpubLargePublicationPreflightSeverity.Error));
        }

        if (diagnostics.Any(static item => item.Severity == EpubLargePublicationPreflightSeverity.Error))
        {
            return Empty(candidate.Id, EpubLargePublicationCandidateStatus.Rejected, diagnostics);
        }

        try
        {
            EpubCorpusSha256 sha256;
            await using (var source = OpenRead(candidate.Path))
            {
                sha256 = new EpubCorpusSha256(Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false)));
            }

            EpubPublicationInspection inspection;
            await using (var source = OpenRead(candidate.Path))
            {
                inspection = await inspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
            }

            diagnostics.AddRange(inspection.Diagnostics.Select(static item => new EpubLargePublicationPreflightDiagnostic(
                item.Code,
                item.Severity switch
                {
                    EpubDiagnosticSeverity.Error => EpubLargePublicationPreflightSeverity.Error,
                    EpubDiagnosticSeverity.Warning => EpubLargePublicationPreflightSeverity.Warning,
                    _ => EpubLargePublicationPreflightSeverity.Information,
                })));
            if (!inspection.IsSuccess)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.InspectionFailed, EpubLargePublicationPreflightSeverity.Error));
            }

            if (inspection.Package?.VersionFamily is null or EpubVersionFamily.Unknown)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.UnknownEpubVersion, EpubLargePublicationPreflightSeverity.Warning));
            }

            var linearSpineCount = inspection.Spine.Count(static item => item.IsLinear);
            var nonLinearSpineCount = inspection.Spine.Length - linearSpineCount;
            var resources = CountResources(inspection.Manifest);
            if (linearSpineCount == 0)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.MissingReadingOrder, EpubLargePublicationPreflightSeverity.Warning));
            }

            if (resources.Xhtml == 0)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.MissingXhtml, EpubLargePublicationPreflightSeverity.Warning));
            }

            var hasToc = inspection.NavigationDocumentPaths.Length > 0
                         || inspection.Manifest.Any(static item => item.MediaType == NcxMediaType);
            if (!hasToc)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.MissingNavigation, EpubLargePublicationPreflightSeverity.Warning));
            }

            var hasLengthEvidence = linearSpineCount >= criteria.MinimumLinearSpineItems
                                    || resources.Xhtml >= criteria.MinimumXhtmlDocuments
                                    || resources.XhtmlBytes >= criteria.MinimumXhtmlBytes
                                    && linearSpineCount >= criteria.MinimumSpineItemsForByteEvidence;
            if (!hasLengthEvidence)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.InsufficientLengthEvidence, EpubLargePublicationPreflightSeverity.Warning));
            }

            if (resources.PresentClassCount < criteria.MinimumResourceClasses)
            {
                diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.InsufficientResourceVariety, EpubLargePublicationPreflightSeverity.Warning));
            }

            var suitable = inspection.IsSuccess
                           && inspection.Package?.VersionFamily is EpubVersionFamily.Epub2 or EpubVersionFamily.Epub3
                           && linearSpineCount > 0
                           && resources.Xhtml > 0
                           && hasToc
                           && hasLengthEvidence
                           && resources.PresentClassCount >= criteria.MinimumResourceClasses;
            return new EpubLargePublicationCandidateResult(
                candidate.Id,
                !inspection.IsSuccess
                    ? EpubLargePublicationCandidateStatus.Rejected
                    : suitable
                        ? EpubLargePublicationCandidateStatus.Suitable
                        : EpubLargePublicationCandidateStatus.Inconclusive,
                sha256,
                inspection.Package?.VersionFamily ?? EpubVersionFamily.Unknown,
                inspection.Manifest.Length,
                inspection.Spine.Length,
                linearSpineCount,
                nonLinearSpineCount,
                inspection.Resources.TotalCompressedBytes,
                inspection.Resources.TotalUncompressedBytes,
                resources,
                new EpubLargePublicationFeatureEvidence(
                    hasToc ? EpubPreflightFeatureStatus.Present : EpubPreflightFeatureStatus.Absent,
                    EpubPreflightFeatureStatus.Unknown,
                    resources.RasterImages > 0 || resources.Svg > 0
                        ? EpubPreflightFeatureStatus.Present
                        : EpubPreflightFeatureStatus.Absent,
                    EpubPreflightFeatureStatus.Unknown,
                    EpubPreflightFeatureStatus.Unknown),
                diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            diagnostics.Add(Diagnostic(EpubLargePublicationPreflightDiagnosticCodes.CandidateUnavailable, EpubLargePublicationPreflightSeverity.Error));
            return Empty(candidate.Id, EpubLargePublicationCandidateStatus.Rejected, diagnostics);
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static EpubLargePublicationResourceCounts CountResources(IEnumerable<EpubManifestItemInfo> manifest)
    {
        var xhtml = 0;
        var css = 0;
        var raster = 0;
        var svg = 0;
        var fonts = 0;
        var audio = 0;
        var other = 0;
        long xhtmlBytes = 0;
        foreach (var item in manifest)
        {
            switch (item.MediaType)
            {
                case XhtmlMediaType:
                    xhtml++;
                    xhtmlBytes = SaturatingAdd(xhtmlBytes, item.UncompressedBytes);
                    break;
                case CssMediaType:
                    css++;
                    break;
                case SvgMediaType:
                    svg++;
                    break;
                case "image/jpeg" or "image/png" or "image/gif" or "image/webp":
                    raster++;
                    break;
                case "font/otf" or "font/ttf" or "font/woff" or "font/woff2"
                    or "application/vnd.ms-opentype" or "application/font-woff":
                    fonts++;
                    break;
                default:
                    if (item.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        audio++;
                    }
                    else
                    {
                        other++;
                    }

                    break;
            }
        }

        return new EpubLargePublicationResourceCounts(xhtml, css, raster, svg, fonts, audio, other, xhtmlBytes);
    }

    private static int FeatureScore(EpubLargePublicationFeatureEvidence features) =>
        new[] { features.TableOfContents, features.Links, features.Images, features.Notes, features.Tables }
            .Count(static item => item == EpubPreflightFeatureStatus.Present);

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right
        ? long.MaxValue
        : left + right;

    private static bool IsInside(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(
            prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static EpubLargePublicationPreflightDiagnostic Diagnostic(
        string code,
        EpubLargePublicationPreflightSeverity severity) => new(code, severity);

    private static EpubLargePublicationCandidateResult Empty(
        EpubCorpusPublicationId id,
        EpubLargePublicationCandidateStatus status,
        IEnumerable<EpubLargePublicationPreflightDiagnostic> diagnostics) => new(
        id,
        status,
        null,
        EpubVersionFamily.Unknown,
        0,
        0,
        0,
        0,
        0,
        0,
        new EpubLargePublicationResourceCounts(0, 0, 0, 0, 0, 0, 0, 0),
        new EpubLargePublicationFeatureEvidence(
            EpubPreflightFeatureStatus.Unknown,
            EpubPreflightFeatureStatus.Unknown,
            EpubPreflightFeatureStatus.Unknown,
            EpubPreflightFeatureStatus.Unknown,
            EpubPreflightFeatureStatus.Unknown),
        diagnostics);
}
