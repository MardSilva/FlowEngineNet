using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Corpus;

/// <summary>Contains stable diagnostics emitted by the automatic large-publication gate.</summary>
public static class EpubLargePublicationGateDiagnosticCodes
{
    public const string CandidateMismatch = "ELG001";
    public const string LegalUseNotDeclared = "ELG002";
    public const string DrmFreeNotDeclared = "ELG003";
    public const string SourceUnavailable = "ELG004";
    public const string SourceLimitExceeded = "ELG005";
    public const string SourceHashMismatch = "ELG006";
    public const string InspectionFailed = "ELG007";
    public const string ImportFailed = "ELG008";
    public const string FidelityFailed = "ELG009";
    public const string FidelityLoss = "ELG010";
    public const string ValidationFailed = "ELG011";
    public const string SerializationFailed = "ELG012";
    public const string IdentityMismatch = "ELG013";
    public const string CanonicalHashMismatch = "ELG014";
    public const string SemanticOrderMismatch = "ELG015";
    public const string LayoutFailed = "ELG016";
    public const string HtmlPackageFailed = "ELG017";
    public const string NonDeterministicResult = "ELG018";
    public const string StructuralAuditPending = "ELG019";
    public const string HumanReviewPending = "ELG020";
    public const string EssentialReferenceBroken = "ELG021";
    public const string EssentialReferenceAmbiguous = "ELG022";
    public const string ReferenceApproximated = "ELG023";
}

/// <summary>Runs one verified EPUB repeatedly through the complete automatic Flow pipeline.</summary>
public sealed class EpubLargePublicationGate : IEpubLargePublicationGate
{
    private static readonly UserReadingPreferences DefaultReadingPreferences = new();
    private readonly IEpubPublicationInspector inspector;
    private readonly IEpubImporter importer;
    private readonly DocumentValidator validator;
    private readonly IEpubFidelityAnalyzer fidelityAnalyzer;
    private readonly IFlowDocumentSerializer serializer;
    private readonly IDocumentIntegrityService integrityService;
    private readonly ILayoutEngine layoutEngine;
    private readonly IHtmlBookPackageRenderer htmlRenderer;
    private readonly EpubImportLimits limits;
    private readonly string temporaryDirectoryRoot;

    public EpubLargePublicationGate()
        : this(
            new EpubPublicationInspector(),
            new EpubImporter(),
            new DocumentValidator(),
            new EpubFidelityAnalyzer(),
            new FlowJsonDocumentSerializer(),
            new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
            new AdaptiveLayoutEngine(),
            new HtmlBookPackageRenderer())
    {
    }

    public EpubLargePublicationGate(
        IEpubPublicationInspector inspector,
        IEpubImporter importer,
        DocumentValidator validator,
        IEpubFidelityAnalyzer fidelityAnalyzer,
        IFlowDocumentSerializer serializer,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IHtmlBookPackageRenderer htmlRenderer,
        EpubImportLimits? limits = null,
        string? temporaryDirectoryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(fidelityAnalyzer);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);
        this.inspector = inspector;
        this.importer = importer;
        this.validator = validator;
        this.fidelityAnalyzer = fidelityAnalyzer;
        this.serializer = serializer;
        this.integrityService = integrityService;
        this.layoutEngine = layoutEngine;
        this.htmlRenderer = htmlRenderer;
        this.limits = limits ?? new EpubImportLimits();
        this.temporaryDirectoryRoot = Path.GetFullPath(temporaryDirectoryRoot ?? Path.GetTempPath());
    }

    public async Task<EpubLargePublicationGateReport> ExecuteAsync(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationGateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        using var workspace = GateWorkspace.Create(temporaryDirectoryRoot, options.CandidateId);
        var total = Stopwatch.StartNew();
        var runs = new List<GateRun>(options.RepetitionCount);
        var preparation = await PrepareSourceAsync(candidate, options, workspace, cancellationToken).ConfigureAwait(false);
        if (!preparation.Success)
        {
            total.Stop();
            return new EpubLargePublicationGateReport(
                options,
                BuildPreparationFailure(options, preparation, total.ElapsedTicks));
        }

        for (var index = 0; index < options.RepetitionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.Add(await ExecuteRunAsync(
                    workspace.SourcePath,
                    workspace.RoundTripPath(index),
                    preparation.SourceBytes,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        total.Stop();
        return new EpubLargePublicationGateReport(
            options,
            BuildResult(options, runs, preparation.Observation, total.ElapsedTicks));
    }

    private async Task<PreparedSource> PrepareSourceAsync(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationGateOptions options,
        GateWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var managed = GC.GetTotalMemory(false);
        var workingSet = Environment.WorkingSet;
        EpubLargePublicationGateDiagnostic? diagnostic = null;
        try
        {
            if (candidate.Id != options.CandidateId)
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.CandidateMismatch, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            if (!candidate.LegalUseDeclared)
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.LegalUseNotDeclared, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            if (!candidate.DrmFreeDeclared)
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.DrmFreeNotDeclared, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            var sourceInfo = new FileInfo(candidate.Path);
            if (!sourceInfo.Exists || IsReparsePoint(sourceInfo))
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.SourceUnavailable, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            if (sourceInfo.Length <= 0 || sourceInfo.Length > limits.MaximumArchiveBytes)
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.SourceLimitExceeded, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            long copiedBytes = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
            await using (var destination = new FileStream(workspace.SourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    copiedBytes += read;
                    if (copiedBytes > limits.MaximumArchiveBytes)
                    {
                        throw new InvalidDataException("The EPUB source exceeded the configured archive byte limit while being copied.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            var observedHash = new EpubCorpusSha256(Convert.ToHexString(hash.GetHashAndReset()));
            if (observedHash != options.ExpectedSourceSha256)
            {
                diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.SourceHashMismatch, EpubLargePublicationGatePhaseKind.Preflight);
                return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
            }

            stopwatch.Stop();
            return new PreparedSource(
                true,
                copiedBytes,
                null,
                Observation(EpubLargePublicationGatePhaseKind.Preflight, stopwatch.ElapsedTicks, managed, workingSet));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.SourceLimitExceeded, EpubLargePublicationGatePhaseKind.Preflight);
            return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostic = Error(EpubLargePublicationGateDiagnosticCodes.SourceUnavailable, EpubLargePublicationGatePhaseKind.Preflight);
            return FailedPreparation(diagnostic, stopwatch, managed, workingSet);
        }
    }

    private async Task<GateRun> ExecuteRunAsync(
        string sourcePath,
        string roundTripPath,
        long sourceBytes,
        CancellationToken cancellationToken)
    {
        var run = new GateRun(sourceBytes);
        EpubPublicationInspection? inspection = null;
        EpubImportResult? import = null;
        FlowDocument? document = null;
        FlowDocument? restored = null;
        DocumentHash? canonicalHash = null;

        inspection = await run.ExecuteAsync(
            EpubLargePublicationGatePhaseKind.Inspection,
            async () =>
            {
                await using var source = OpenSource(sourcePath);
                return await inspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
            },
            EpubLargePublicationGateDiagnosticCodes.InspectionFailed,
            cancellationToken).ConfigureAwait(false);
        if (inspection is not null)
        {
            run.EpubVersion = inspection.Package?.VersionFamily;
            run.ManifestItemCount = inspection.Manifest.Length;
            run.SpineItemCount = inspection.Spine.Length;
            run.ApplyDiagnostics(EpubLargePublicationGatePhaseKind.Inspection, inspection.Diagnostics);
            if (!inspection.IsSuccess)
            {
                run.Fail(EpubLargePublicationGatePhaseKind.Inspection, EpubLargePublicationGateDiagnosticCodes.InspectionFailed);
            }
        }

        import = await run.ExecuteAsync(
            EpubLargePublicationGatePhaseKind.Import,
            async () =>
            {
                await using var source = OpenSource(sourcePath);
                return await importer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
            },
            EpubLargePublicationGateDiagnosticCodes.ImportFailed,
            cancellationToken).ConfigureAwait(false);
        if (import is not null)
        {
            run.ApplyDiagnostics(EpubLargePublicationGatePhaseKind.Import, import.Diagnostics);
            document = import.Document;
            if (!import.IsSuccess || document is null)
            {
                run.Fail(EpubLargePublicationGatePhaseKind.Import, EpubLargePublicationGateDiagnosticCodes.ImportFailed);
            }
        }

        if (document is null)
        {
            run.SkipDependentPhases(EpubLargePublicationGatePhaseKind.Fidelity);
            return run;
        }

        var fidelity = run.Execute(
            EpubLargePublicationGatePhaseKind.Fidelity,
            () => fidelityAnalyzer.Analyze(import!, cancellationToken),
            EpubLargePublicationGateDiagnosticCodes.FidelityFailed,
            cancellationToken);
        if (fidelity is not null)
        {
            run.FidelitySourceUnitCount = fidelity.Summary.SourceUnitCount;
            run.FidelityLostUnitCount = fidelity.Summary.LostCount;
            var severeLoss = fidelity.Findings.Any(static item =>
                item.Status == EpubFidelityStatus.Lost
                && item.Impact is EpubFidelityImpact.Major or EpubFidelityImpact.Critical);
            if (severeLoss)
            {
                run.Fail(EpubLargePublicationGatePhaseKind.Fidelity, EpubLargePublicationGateDiagnosticCodes.FidelityLoss);
            }
            else if (fidelity.Summary.LostCount > 0)
            {
                run.Warn(EpubLargePublicationGatePhaseKind.Fidelity, EpubLargePublicationGateDiagnosticCodes.FidelityLoss);
            }
        }

        var validation = run.Execute(
            EpubLargePublicationGatePhaseKind.Validation,
            () => validator.Validate(document),
            EpubLargePublicationGateDiagnosticCodes.ValidationFailed,
            cancellationToken);
        if (validation is not null)
        {
            run.ValidationDiagnosticCount = validation.Diagnostics.Length;
            if (!validation.IsValid)
            {
                run.Fail(EpubLargePublicationGatePhaseKind.Validation, EpubLargePublicationGateDiagnosticCodes.ValidationFailed);
            }
            else if (validation.Diagnostics.Length > 0)
            {
                run.Warn(EpubLargePublicationGatePhaseKind.Validation, EpubLargePublicationGateDiagnosticCodes.ValidationFailed);
            }
        }

        var roundTrip = await run.ExecuteAsync(
            EpubLargePublicationGatePhaseKind.Serialization,
            () => RoundTripAsync(document, roundTripPath, cancellationToken),
            EpubLargePublicationGateDiagnosticCodes.SerializationFailed,
            cancellationToken).ConfigureAwait(false);
        if (roundTrip is not null)
        {
            restored = roundTrip.Document;
            run.FlowJsonBytes = roundTrip.Bytes;
        }

        if (restored is not null)
        {
            canonicalHash = run.Execute(
                EpubLargePublicationGatePhaseKind.Integrity,
                () => VerifyRoundTrip(document, restored, run),
                EpubLargePublicationGateDiagnosticCodes.CanonicalHashMismatch,
                cancellationToken);
        }
        else
        {
            run.Skip(EpubLargePublicationGatePhaseKind.Integrity);
        }

        run.CaptureDocumentEvidence(document, canonicalHash);
        if (validation?.IsValid != true || canonicalHash is null)
        {
            run.SkipRenderingPhases();
            return run;
        }

        var integrity = new HtmlBookIntegrity(
            canonicalHash.Algorithm,
            canonicalHash.Hash,
            canonicalHash.CanonicalizationVersion);
        var mobile = await RenderTargetAsync(
                run,
                document,
                new LayoutContext(390, 844, DeviceClass.Phone),
                EpubLargePublicationGatePhaseKind.MobileLayout,
                EpubLargePublicationGatePhaseKind.MobileHtmlPackage,
                integrity,
                cancellationToken)
            .ConfigureAwait(false);
        var desktop = await RenderTargetAsync(
                run,
                document,
                new LayoutContext(1600, 1000, DeviceClass.Desktop),
                EpubLargePublicationGatePhaseKind.DesktopLayout,
                EpubLargePublicationGatePhaseKind.DesktopHtmlPackage,
                integrity,
                cancellationToken)
            .ConfigureAwait(false);
        var audit = run.Execute(
            EpubLargePublicationGatePhaseKind.StructuralAudit,
            () => EpubLargePublicationAuditor.Audit(
                new EpubLargePublicationAuditInput(
                    import?.ProcessingReport,
                    import?.SourceMap,
                    document,
                    restored!,
                    mobile?.Layout,
                    mobile?.Package,
                    desktop?.Layout,
                    desktop?.Package),
                cancellationToken),
            EpubLargePublicationGateDiagnosticCodes.EssentialReferenceBroken,
            cancellationToken);
        if (audit is not null)
        {
            run.ReferenceAudits = audit.Audits;
            if (audit.HasEssentialFailure)
            {
                run.Fail(
                    EpubLargePublicationGatePhaseKind.StructuralAudit,
                    audit.Audits.Any(static item => item.Essential && item.Counts.Ambiguous > 0)
                        ? EpubLargePublicationGateDiagnosticCodes.EssentialReferenceAmbiguous
                        : EpubLargePublicationGateDiagnosticCodes.EssentialReferenceBroken);
            }
            else if (audit.HasApproximation)
            {
                run.Warn(
                    EpubLargePublicationGatePhaseKind.StructuralAudit,
                    EpubLargePublicationGateDiagnosticCodes.ReferenceApproximated);
            }
        }

        return run;
    }

    private async Task<RoundTripResult> RoundTripAsync(
        FlowDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await serializer.SerializeAsync(document, output, cancellationToken).ConfigureAwait(false);
            }

            var length = checked((int)new FileInfo(path).Length);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            var restored = await serializer.DeserializeAsync(input, cancellationToken).ConfigureAwait(false);
            return new RoundTripResult(restored, length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private DocumentHash VerifyRoundTrip(FlowDocument original, FlowDocument restored, GateRun run)
    {
        if (original.Identity != restored.Identity)
        {
            run.Fail(EpubLargePublicationGatePhaseKind.Integrity, EpubLargePublicationGateDiagnosticCodes.IdentityMismatch);
        }

        var originalIds = original.Index.Locations.Select(static item => item.Node.Id.Value).ToArray();
        var restoredIds = restored.Index.Locations.Select(static item => item.Node.Id.Value).ToArray();
        if (!originalIds.SequenceEqual(restoredIds, StringComparer.Ordinal))
        {
            run.Fail(EpubLargePublicationGatePhaseKind.Integrity, EpubLargePublicationGateDiagnosticCodes.SemanticOrderMismatch);
        }

        var originalHash = integrityService.ComputeHash(original);
        var restoredHash = integrityService.ComputeHash(restored);
        if (originalHash != restoredHash)
        {
            run.Fail(EpubLargePublicationGatePhaseKind.Integrity, EpubLargePublicationGateDiagnosticCodes.CanonicalHashMismatch);
        }

        return originalHash;
    }

    private async Task<RenderTargetResult?> RenderTargetAsync(
        GateRun run,
        FlowDocument document,
        LayoutContext context,
        EpubLargePublicationGatePhaseKind layoutPhase,
        EpubLargePublicationGatePhaseKind packagePhase,
        HtmlBookIntegrity integrity,
        CancellationToken cancellationToken)
    {
        var layout = run.Execute(
            layoutPhase,
            () =>
            {
                var value = layoutEngine.Layout(document, context);
                EnsureLayoutPreservesIds(document, value);
                return value;
            },
            EpubLargePublicationGateDiagnosticCodes.LayoutFailed,
            cancellationToken);
        if (layout is null)
        {
            run.Skip(packagePhase);
            return null;
        }

        run.SetLayoutNodeCount(layoutPhase, EnumerateLayoutNodes(layout.Nodes).Count());
        var packageEvidence = await run.ExecuteAsync(
            packagePhase,
            () => Task.Run(() =>
            {
                var package = htmlRenderer.Render(
                    document,
                    layout,
                    DefaultReadingPreferences,
                    integrity,
                    new HtmlBookPackageOptions(),
                    cancellationToken);
                return HtmlBookPackageVerifier.Verify(package, cancellationToken);
            }, cancellationToken),
            EpubLargePublicationGateDiagnosticCodes.HtmlPackageFailed,
            cancellationToken).ConfigureAwait(false);
        if (packageEvidence is not null)
        {
            run.SetPackageEvidence(packagePhase, packageEvidence);
            return new RenderTargetResult(layout, packageEvidence);
        }

        return new RenderTargetResult(layout, null);
    }

    private static EpubLargePublicationGateResult BuildResult(
        EpubLargePublicationGateOptions options,
        IReadOnlyList<GateRun> runs,
        EpubLargePublicationGatePhaseObservation preparationObservation,
        long totalDurationTicks)
    {
        var first = runs[0];
        var deterministic = runs.Skip(1).All(run => run.StableFingerprint() == first.StableFingerprint());
        var phases = Enum.GetValues<EpubLargePublicationGatePhaseKind>()
            .Where(static phase => phase is not EpubLargePublicationGatePhaseKind.Preflight
                and not EpubLargePublicationGatePhaseKind.DeterminismComparison
                and not EpubLargePublicationGatePhaseKind.HumanReview)
            .Select(phase => new EpubLargePublicationGatePhase(
                phase,
                AggregateStatus(runs.Select(run => run.Status(phase)))))
            .Prepend(new EpubLargePublicationGatePhase(
                EpubLargePublicationGatePhaseKind.Preflight,
                EpubLargePublicationGateStatus.Passed))
            .Append(new EpubLargePublicationGatePhase(
                EpubLargePublicationGatePhaseKind.DeterminismComparison,
                deterministic ? EpubLargePublicationGateStatus.Passed : EpubLargePublicationGateStatus.Failed))
            .Append(new EpubLargePublicationGatePhase(
                EpubLargePublicationGatePhaseKind.HumanReview,
                options.RequireHumanReview
                    ? EpubLargePublicationGateStatus.NotStarted
                    : EpubLargePublicationGateStatus.Skipped))
            .ToArray();

        var diagnostics = runs.SelectMany(static run => run.Diagnostics).ToList();
        if (!deterministic)
        {
            diagnostics.Add(Error(
                EpubLargePublicationGateDiagnosticCodes.NonDeterministicResult,
                EpubLargePublicationGatePhaseKind.DeterminismComparison));
        }

        if (options.RequireHumanReview)
        {
            diagnostics.Add(new EpubLargePublicationGateDiagnostic(
                EpubLargePublicationGateDiagnosticCodes.HumanReviewPending,
                EpubLargePublicationGateDiagnosticSeverity.Information,
                EpubLargePublicationGatePhaseKind.HumanReview));
        }

        var automaticChecks = phases
            .Where(static phase => phase.Kind is not EpubLargePublicationGatePhaseKind.HumanReview)
            .Select(static phase => new EpubLargePublicationGateCheck(
                $"phase.{Token(phase.Kind)}",
                phase.Kind,
                EpubLargePublicationGateCheckKind.Automatic,
                phase.Status));
        var humanChecks = new[] { "review.beginning", "review.middle", "review.end" }
            .Select(id => new EpubLargePublicationGateCheck(
                id,
                EpubLargePublicationGatePhaseKind.HumanReview,
                EpubLargePublicationGateCheckKind.HumanReview,
                options.RequireHumanReview
                    ? EpubLargePublicationGateStatus.NotStarted
                    : EpubLargePublicationGateStatus.Skipped));
        var requiredFailed = phases.Any(static phase => IsRequiredAutomaticPhase(phase.Kind)
            && phase.Status != EpubLargePublicationGateStatus.Passed
            && phase.Status != EpubLargePublicationGateStatus.PassedWithWarnings);
        var status = requiredFailed
            ? EpubLargePublicationGateStatus.Failed
            : options.RequireHumanReview
                ? EpubLargePublicationGateStatus.Inconclusive
                : diagnostics.Any(static item => item.Severity == EpubLargePublicationGateDiagnosticSeverity.Warning)
                    ? EpubLargePublicationGateStatus.PassedWithWarnings
                    : EpubLargePublicationGateStatus.Passed;
        return new EpubLargePublicationGateResult(
            status,
            first.Evidence(),
            phases,
            automaticChecks,
            humanChecks,
            diagnostics,
            AggregateEnvironment(runs, preparationObservation, totalDurationTicks));
    }

    private static EpubLargePublicationGateResult BuildPreparationFailure(
        EpubLargePublicationGateOptions options,
        PreparedSource preparation,
        long totalDurationTicks)
    {
        var humanChecks = new[] { "review.beginning", "review.middle", "review.end" }
            .Select(id => new EpubLargePublicationGateCheck(
                id,
                EpubLargePublicationGatePhaseKind.HumanReview,
                EpubLargePublicationGateCheckKind.HumanReview,
                EpubLargePublicationGateStatus.Skipped));
        return new EpubLargePublicationGateResult(
            EpubLargePublicationGateStatus.Failed,
            new EpubLargePublicationGateEvidence(),
            [new(EpubLargePublicationGatePhaseKind.Preflight, EpubLargePublicationGateStatus.Failed)],
            [new(
                "phase.preflight",
                EpubLargePublicationGatePhaseKind.Preflight,
                EpubLargePublicationGateCheckKind.Automatic,
                EpubLargePublicationGateStatus.Failed)],
            humanChecks,
            preparation.Diagnostic is null ? [] : [preparation.Diagnostic],
            new EpubLargePublicationGateEnvironmentObservations(
                totalDurationTicks,
                preparation.Observation.ApproximateManagedBytes,
                preparation.Observation.ApproximateWorkingSetBytes,
                [preparation.Observation]));
    }

    private static EpubLargePublicationGateEnvironmentObservations AggregateEnvironment(
        IEnumerable<GateRun> runs,
        EpubLargePublicationGatePhaseObservation preparation,
        long totalDurationTicks)
    {
        var observations = runs.SelectMany(static run => run.Observations)
            .Append(preparation)
            .GroupBy(static item => item.Phase)
            .Select(static group => new EpubLargePublicationGatePhaseObservation(
                group.Key,
                group.Sum(static item => item.DurationTicks),
                group.Max(static item => item.ApproximateManagedBytes),
                group.Max(static item => item.ApproximateWorkingSetBytes)))
            .ToArray();
        return new EpubLargePublicationGateEnvironmentObservations(
            totalDurationTicks,
            observations.Max(static item => item.ApproximateManagedBytes),
            observations.Max(static item => item.ApproximateWorkingSetBytes),
            observations);
    }

    private static EpubLargePublicationGateStatus AggregateStatus(IEnumerable<EpubLargePublicationGateStatus> values)
    {
        var materialized = values.ToArray();
        foreach (var status in new[]
                 {
                     EpubLargePublicationGateStatus.Failed,
                     EpubLargePublicationGateStatus.Inconclusive,
                     EpubLargePublicationGateStatus.Skipped,
                     EpubLargePublicationGateStatus.NotStarted,
                     EpubLargePublicationGateStatus.PassedWithWarnings,
                     EpubLargePublicationGateStatus.Passed,
                 })
        {
            if (materialized.Contains(status))
            {
                return status;
            }
        }

        return EpubLargePublicationGateStatus.NotStarted;
    }

    private static bool IsRequiredAutomaticPhase(EpubLargePublicationGatePhaseKind phase) => phase is
        EpubLargePublicationGatePhaseKind.Preflight
        or EpubLargePublicationGatePhaseKind.Inspection
        or EpubLargePublicationGatePhaseKind.Import
        or EpubLargePublicationGatePhaseKind.Fidelity
        or EpubLargePublicationGatePhaseKind.Validation
        or EpubLargePublicationGatePhaseKind.Serialization
        or EpubLargePublicationGatePhaseKind.Integrity
        or EpubLargePublicationGatePhaseKind.MobileLayout
        or EpubLargePublicationGatePhaseKind.MobileHtmlPackage
        or EpubLargePublicationGatePhaseKind.DesktopLayout
        or EpubLargePublicationGatePhaseKind.DesktopHtmlPackage
        or EpubLargePublicationGatePhaseKind.StructuralAudit
        or EpubLargePublicationGatePhaseKind.DeterminismComparison;

    private static FileStream OpenSource(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);

    private static bool IsReparsePoint(FileInfo file) =>
        (file.Attributes & FileAttributes.ReparsePoint) != 0;

    private static PreparedSource FailedPreparation(
        EpubLargePublicationGateDiagnostic diagnostic,
        Stopwatch stopwatch,
        long managed,
        long workingSet)
    {
        stopwatch.Stop();
        return new PreparedSource(
            false,
            0,
            diagnostic,
            Observation(EpubLargePublicationGatePhaseKind.Preflight, stopwatch.ElapsedTicks, managed, workingSet));
    }

    private static EpubLargePublicationGatePhaseObservation Observation(
        EpubLargePublicationGatePhaseKind phase,
        long durationTicks,
        long initialManaged,
        long initialWorkingSet) => new(
        phase,
        durationTicks,
        Math.Max(initialManaged, GC.GetTotalMemory(false)),
        Math.Max(initialWorkingSet, Environment.WorkingSet));

    private static EpubLargePublicationGateDiagnostic Error(
        string code,
        EpubLargePublicationGatePhaseKind phase) => new(
        code,
        EpubLargePublicationGateDiagnosticSeverity.Error,
        phase);

    private static void EnsureLayoutPreservesIds(FlowDocument document, LayoutDocument layout)
    {
        var semanticIds = document.Index.Locations.Select(static item => item.Node.Id).ToHashSet();
        var layoutIds = EnumerateLayoutNodes(layout.Nodes).Select(static item => item.SemanticId).ToHashSet();
        if (!semanticIds.SetEquals(layoutIds))
        {
            throw new InvalidDataException("Layout did not preserve the complete semantic node ID set.");
        }
    }

    private static IEnumerable<LayoutNode> EnumerateLayoutNodes(IEnumerable<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateLayoutNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static EpubCorpusSha256 HashValues(IEnumerable<string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return new EpubCorpusSha256(Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static string Token<T>(T value)
        where T : struct, Enum => string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private sealed record PreparedSource(
        bool Success,
        long SourceBytes,
        EpubLargePublicationGateDiagnostic? Diagnostic,
        EpubLargePublicationGatePhaseObservation Observation);

    private sealed record RoundTripResult(FlowDocument Document, int Bytes);

    private sealed record RenderTargetResult(LayoutDocument Layout, HtmlBookPackageEvidence? Package);

    private sealed class GateRun(long sourceBytes)
    {
        private readonly Dictionary<EpubLargePublicationGatePhaseKind, EpubLargePublicationGateStatus> statuses = [];
        internal readonly List<EpubLargePublicationGateDiagnostic> Diagnostics = [];
        internal readonly List<EpubLargePublicationGatePhaseObservation> Observations = [];
        internal EpubVersionFamily? EpubVersion;
        internal int ManifestItemCount;
        internal int SpineItemCount;
        internal int ChapterCount;
        internal int ImportedNodeCount;
        internal int ImportedAssetCount;
        internal long ImportedCharacterCount;
        internal int ValidationDiagnosticCount;
        internal long FidelitySourceUnitCount;
        internal long FidelityLostUnitCount;
        internal int FlowJsonBytes;
        internal EpubCorpusSha256? CanonicalHash;
        internal EpubCorpusSha256? ReadingOrderHash;
        internal EpubCorpusSha256? AnchorSetHash;
        internal int MobileLayoutNodeCount;
        internal int MobileHtmlFileCount;
        internal long MobileHtmlBytes;
        internal int DesktopLayoutNodeCount;
        internal int DesktopHtmlFileCount;
        internal long DesktopHtmlBytes;
        internal System.Collections.Immutable.ImmutableArray<EpubLargePublicationReferenceAudit> ReferenceAudits = [];

        internal EpubLargePublicationGateStatus Status(EpubLargePublicationGatePhaseKind phase) =>
            statuses.GetValueOrDefault(phase, EpubLargePublicationGateStatus.NotStarted);

        internal async Task<T?> ExecuteAsync<T>(
            EpubLargePublicationGatePhaseKind phase,
            Func<Task<T>> action,
            string failureCode,
            CancellationToken cancellationToken)
            where T : class
        {
            var stopwatch = Stopwatch.StartNew();
            var managed = GC.GetTotalMemory(false);
            var workingSet = Environment.WorkingSet;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await action().ConfigureAwait(false);
                if (Status(phase) != EpubLargePublicationGateStatus.Failed)
                {
                    statuses[phase] = EpubLargePublicationGateStatus.Passed;
                }

                return value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail(phase, failureCode);
                return null;
            }
            finally
            {
                stopwatch.Stop();
                Observations.Add(Observation(phase, stopwatch.ElapsedTicks, managed, workingSet));
            }
        }

        internal T? Execute<T>(
            EpubLargePublicationGatePhaseKind phase,
            Func<T> action,
            string failureCode,
            CancellationToken cancellationToken)
            where T : class
        {
            var stopwatch = Stopwatch.StartNew();
            var managed = GC.GetTotalMemory(false);
            var workingSet = Environment.WorkingSet;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = action();
                if (Status(phase) != EpubLargePublicationGateStatus.Failed)
                {
                    statuses[phase] = EpubLargePublicationGateStatus.Passed;
                }

                return value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail(phase, failureCode);
                return null;
            }
            finally
            {
                stopwatch.Stop();
                Observations.Add(Observation(phase, stopwatch.ElapsedTicks, managed, workingSet));
            }
        }

        internal void ApplyDiagnostics(
            EpubLargePublicationGatePhaseKind phase,
            IEnumerable<EpubDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                var severity = diagnostic.Severity switch
                {
                    EpubDiagnosticSeverity.Error => EpubLargePublicationGateDiagnosticSeverity.Error,
                    EpubDiagnosticSeverity.Warning => EpubLargePublicationGateDiagnosticSeverity.Warning,
                    _ => EpubLargePublicationGateDiagnosticSeverity.Information,
                };
                var safeCode = diagnostic.Code.All(static character => character is >= 'A' and <= 'Z' or >= '0' and <= '9')
                    ? diagnostic.Code
                    : phase == EpubLargePublicationGatePhaseKind.Inspection
                        ? EpubLargePublicationGateDiagnosticCodes.InspectionFailed
                        : EpubLargePublicationGateDiagnosticCodes.ImportFailed;
                Diagnostics.Add(new EpubLargePublicationGateDiagnostic(safeCode, severity, phase));
                if (severity == EpubLargePublicationGateDiagnosticSeverity.Error)
                {
                    statuses[phase] = EpubLargePublicationGateStatus.Failed;
                }
                else if (severity == EpubLargePublicationGateDiagnosticSeverity.Warning
                         && Status(phase) == EpubLargePublicationGateStatus.Passed)
                {
                    statuses[phase] = EpubLargePublicationGateStatus.PassedWithWarnings;
                }
            }
        }

        internal void Fail(EpubLargePublicationGatePhaseKind phase, string code)
        {
            statuses[phase] = EpubLargePublicationGateStatus.Failed;
            Diagnostics.Add(Error(code, phase));
        }

        internal void Warn(EpubLargePublicationGatePhaseKind phase, string code)
        {
            if (Status(phase) != EpubLargePublicationGateStatus.Failed)
            {
                statuses[phase] = EpubLargePublicationGateStatus.PassedWithWarnings;
            }

            Diagnostics.Add(new EpubLargePublicationGateDiagnostic(
                code,
                EpubLargePublicationGateDiagnosticSeverity.Warning,
                phase));
        }

        internal void Skip(EpubLargePublicationGatePhaseKind phase)
        {
            if (!statuses.ContainsKey(phase))
            {
                statuses[phase] = EpubLargePublicationGateStatus.Skipped;
            }
        }

        internal void SkipDependentPhases(EpubLargePublicationGatePhaseKind first)
        {
            foreach (var phase in Enum.GetValues<EpubLargePublicationGatePhaseKind>().Where(phase => phase >= first))
            {
                if (phase is not EpubLargePublicationGatePhaseKind.StructuralAudit
                    and not EpubLargePublicationGatePhaseKind.DeterminismComparison
                    and not EpubLargePublicationGatePhaseKind.HumanReview)
                {
                    Skip(phase);
                }
            }
        }

        internal void SkipRenderingPhases()
        {
            Skip(EpubLargePublicationGatePhaseKind.MobileLayout);
            Skip(EpubLargePublicationGatePhaseKind.MobileHtmlPackage);
            Skip(EpubLargePublicationGatePhaseKind.DesktopLayout);
            Skip(EpubLargePublicationGatePhaseKind.DesktopHtmlPackage);
        }

        internal void CaptureDocumentEvidence(FlowDocument document, DocumentHash? hash)
        {
            var orderedIds = document.Index.Locations.Select(static item => item.Node.Id.Value).ToArray();
            ChapterCount = document.Index.Locations.Count(static item => item.Node is Chapter);
            ImportedNodeCount = orderedIds.Length;
            ImportedAssetCount = document.Assets.Count;
            ImportedCharacterCount = EpubProducedContentMetrics.CountCharacters(document);
            ReadingOrderHash = HashValues(orderedIds);
            AnchorSetHash = HashValues(orderedIds.Order(StringComparer.Ordinal));
            if (hash is not null && EpubCorpusSha256.TryParse(hash.Hash, out var parsed))
            {
                CanonicalHash = parsed;
            }
        }

        internal void SetLayoutNodeCount(EpubLargePublicationGatePhaseKind phase, int count)
        {
            if (phase == EpubLargePublicationGatePhaseKind.MobileLayout)
            {
                MobileLayoutNodeCount = count;
            }
            else
            {
                DesktopLayoutNodeCount = count;
            }
        }

        internal void SetPackageEvidence(
            EpubLargePublicationGatePhaseKind phase,
            HtmlBookPackageEvidence evidence)
        {
            if (phase == EpubLargePublicationGatePhaseKind.MobileHtmlPackage)
            {
                MobileHtmlFileCount = evidence.FileCount;
                MobileHtmlBytes = evidence.Bytes;
            }
            else
            {
                DesktopHtmlFileCount = evidence.FileCount;
                DesktopHtmlBytes = evidence.Bytes;
            }
        }

        internal EpubLargePublicationGateEvidence Evidence() => new(
            EpubVersion,
            sourceBytes,
            ManifestItemCount,
            SpineItemCount,
            ChapterCount,
            ImportedNodeCount,
            ImportedAssetCount,
            ImportedCharacterCount,
            ValidationDiagnosticCount,
            FidelitySourceUnitCount,
            FidelityLostUnitCount,
            FlowJsonBytes,
            CanonicalHash,
            ReadingOrderHash,
            AnchorSetHash,
            MobileLayoutNodeCount,
            MobileHtmlFileCount,
            MobileHtmlBytes,
            DesktopLayoutNodeCount,
            DesktopHtmlFileCount,
            DesktopHtmlBytes)
        {
            ReferenceAudits = ReferenceAudits,
        };

        internal string StableFingerprint()
        {
            var evidence = Evidence();
            var phaseValues = Enum.GetValues<EpubLargePublicationGatePhaseKind>()
                .Where(static phase => phase is not EpubLargePublicationGatePhaseKind.DeterminismComparison
                    and not EpubLargePublicationGatePhaseKind.HumanReview)
                .Select(phase => $"{phase}:{Status(phase)}");
            var diagnosticValues = Diagnostics
                .Select(static item => $"{item.Phase}:{item.Code}:{item.Severity}")
                .Order(StringComparer.Ordinal);
            var auditValues = ReferenceAudits.Select(static item =>
                $"{item.Kind}:{item.Applicability}:{item.Essential}:{item.Counts.Found}:{item.Counts.Resolved}:{item.Counts.Broken}:{item.Counts.Ambiguous}:{item.Counts.Approximated}:{item.Counts.Skipped}");
            return string.Join('|', new[] { evidence.ToString() }.Concat(phaseValues).Concat(diagnosticValues).Concat(auditValues));
        }

    }

    private sealed class GateWorkspace : IDisposable
    {
        private GateWorkspace(string path, string sourcePath)
        {
            Path = path;
            SourcePath = sourcePath;
        }

        internal string Path { get; }

        internal string SourcePath { get; }

        internal static GateWorkspace Create(string root, EpubCorpusPublicationId id)
        {
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, $"flow-large-gate-{id.Value}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new GateWorkspace(path, System.IO.Path.Combine(path, "source.epub"));
        }

        internal string RoundTripPath(int repetition) =>
            System.IO.Path.Combine(Path, $"roundtrip-{repetition}.flow.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
