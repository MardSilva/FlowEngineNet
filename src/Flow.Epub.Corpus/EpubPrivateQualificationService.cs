using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Runs neutral private candidates through repeated corpus qualification.</summary>
public sealed class EpubPrivateQualificationService : IEpubPrivateQualificationService
{
    private static readonly string[] RequiredResults =
    [
        "inspection-success",
        "import-success",
        "valid-flow-document",
        "roundtrip-stable",
        "canonical-hash-stable",
        "mobile-layout",
        "desktop-layout",
        "html-book-package",
    ];

    private readonly IEpubPrivateInventoryService inventoryService;
    private readonly EpubCorpusQualificationService qualificationService;

    public EpubPrivateQualificationService(
        IEpubPrivateInventoryService? inventoryService = null,
        EpubCorpusQualificationService? qualificationService = null)
    {
        this.inventoryService = inventoryService ?? new EpubPrivateInventoryService();
        this.qualificationService = qualificationService ?? new EpubCorpusQualificationService();
    }

    public async Task<EpubPrivateQualificationReport> QualifyAsync(
        string sourceDirectory,
        string repositoryRoot,
        EpubPrivateQualificationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(options);
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var protectedRepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        if (IsWithinRoot(protectedRepositoryRoot, sourceRoot))
        {
            throw new ArgumentException("The private source directory must remain outside the repository.", nameof(sourceDirectory));
        }

        var inventory = await inventoryService
            .InventoryAsync(sourceRoot, options.InventoryOptions, cancellationToken)
            .ConfigureAwait(false);
        var eligible = inventory.Publications.Where(IsEligible).ToArray();
        EpubCorpusQualificationResult? qualification = null;
        if (eligible.Length > 0)
        {
            var manifest = new EpubCorpusManifest(
                EpubCorpusManifest.CurrentFormat,
                eligible.Select(CreateManifestPublication));
            var discovery = new EpubCorpusDiscoveryOptions(
                protectedRepositoryRoot,
                sourceRoot,
                options.InventoryOptions.MaximumCandidateFiles,
                options.InventoryOptions.MaximumRecursionDepth,
                new EpubImportLimits().MaximumArchiveBytes);
            qualification = await qualificationService
                .QualifyAsync(manifest, discovery, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var results = inventory.Publications.Select(item => CreateResult(item, qualification)).ToArray();
        return new EpubPrivateQualificationReport(
            qualification?.IsDeterministic ?? true,
            results);
    }

    private static bool IsEligible(EpubPrivateInventoryItem item) =>
        item.Sha256 is not null
        && item.Status is EpubPrivateInventoryStatus.Ready or EpubPrivateInventoryStatus.ReviewRequired
        && item.Protection != EpubPrivateInventoryProtectionStatus.UnsupportedEncryption;

    private static EpubCorpusPublication CreateManifestPublication(EpubPrivateInventoryItem item) => new(
        item.Id,
        item.Id.Value,
        "private-local-inventory",
        new EpubCorpusLicense(
            "Private local use",
            "The caller explicitly declared legal local use and absence of DRM."),
        EpubCorpusPublicationKind.LocalNonRedistributablePublication,
        EpubCorpusRedistribution.Prohibited,
        $"private/{item.Id.Value}.epub",
        item.EpubVersion,
        item.FileBytes,
        item.Languages,
        [],
        [],
        RequiredResults,
        [],
        item.Sha256!.Value);

    private static EpubPrivateQualificationItem CreateResult(
        EpubPrivateInventoryItem inventory,
        EpubCorpusQualificationResult? qualification)
    {
        if (!IsEligible(inventory))
        {
            return CreateSkippedResult(inventory);
        }

        var execution = qualification!.Report.Publications.Single(item => item.Id == inventory.Id);
        var semantic = execution.Evidence.Semantic;
        var status = !qualification.IsDeterministic
            ? EpubPrivateQualificationStatus.Nondeterministic
            : execution.Evidence.FidelityLostUnitCount > 0
                ? EpubPrivateQualificationStatus.Failed
            : execution.Status switch
            {
                EpubCorpusExecutionStatus.Passed => EpubPrivateQualificationStatus.Passed,
                EpubCorpusExecutionStatus.Failed => EpubPrivateQualificationStatus.Failed,
                _ => EpubPrivateQualificationStatus.Inconclusive,
            };
        var evidence = new EpubPrivateQualificationEvidence(
            execution.Evidence.ManifestItemCount,
            execution.Evidence.SpineItemCount,
            execution.Evidence.ImportedNodeCount,
            execution.Evidence.ImportedAssetCount,
            execution.Evidence.ValidationDiagnosticCount,
            execution.Evidence.FidelitySourceUnitCount,
            execution.Evidence.FidelityLostUnitCount,
            execution.Evidence.CanonicalHash,
            execution.Evidence.FlowJsonBytes,
            execution.Evidence.MobileLayoutNodeCount,
            execution.Evidence.DesktopLayoutNodeCount,
            execution.Evidence.HtmlPackageCount,
            execution.Evidence.HtmlFileCount,
            execution.Evidence.HtmlBytes,
            semantic?.ChapterCount ?? 0,
            semantic?.HeadingCount ?? 0,
            semantic?.ParagraphCount ?? 0,
            semantic?.TableOfContentsEntryCount ?? 0,
            semantic?.InternalLinkCount ?? 0,
            semantic?.FigureCount ?? 0,
            semantic?.FootnoteCount ?? 0,
            semantic?.FootnoteReferenceCount ?? 0,
            semantic?.TableCount ?? 0,
            semantic?.TableCellCount ?? 0);
        var diagnostics = execution.Diagnostics
            .GroupBy(static item => new
            {
                Code = item.SourceCode ?? item.Code,
                item.Severity,
                item.Phase,
            })
            .Select(static group => new EpubPrivateQualificationDiagnosticCount(
                group.Key.Code,
                group.Key.Severity,
                group.Key.Phase,
                group.Sum(static item => item.Count)));
        return new EpubPrivateQualificationItem(
            inventory.Id,
            inventory.Sha256,
            inventory.Status,
            status,
            eligible: true,
            qualification.IsDeterministic,
            execution.CompletedPhases,
            evidence,
            diagnostics);
    }

    private static EpubPrivateQualificationItem CreateSkippedResult(EpubPrivateInventoryItem item)
    {
        var status = item.Status switch
        {
            EpubPrivateInventoryStatus.Protected => EpubPrivateQualificationStatus.SkippedProtected,
            EpubPrivateInventoryStatus.Corrupt => EpubPrivateQualificationStatus.SkippedCorrupt,
            _ => EpubPrivateQualificationStatus.SkippedUnsuitable,
        };
        var diagnostics = item.Diagnostics.Select(static diagnostic => new EpubPrivateQualificationDiagnosticCount(
            diagnostic.Code,
            diagnostic.Severity switch
            {
                EpubPrivateInventoryDiagnosticSeverity.Error => EpubCorpusExecutionDiagnosticSeverity.Error,
                EpubPrivateInventoryDiagnosticSeverity.Warning => EpubCorpusExecutionDiagnosticSeverity.Warning,
                _ => EpubCorpusExecutionDiagnosticSeverity.Information,
            },
            null,
            1));
        return new EpubPrivateQualificationItem(
            item.Id,
            item.Sha256,
            item.Status,
            status,
            eligible: false,
            stableAcrossRepeatedRuns: false,
            [],
            EpubPrivateQualificationEvidence.Empty,
            diagnostics);
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "."
               || (!Path.IsPathFullyQualified(relative)
                   && relative != ".."
                   && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                   && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
