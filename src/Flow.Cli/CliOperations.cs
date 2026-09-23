using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Flow.Documents;
using Flow.Epub;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Cli;

/// <summary>Executes typed CLI commands against the Flow domain services.</summary>
public sealed class CliOperations
{
    private readonly IFlowDocumentSerializer _serializer;
    private readonly IEpubImporter _epubImporter;
    private readonly IEpubPublicationInspector _epubInspector;
    private readonly DocumentValidator _validator;
    private readonly IDocumentIntegrityService _integrityService;
    private readonly ILayoutEngine _layoutEngine;
    private readonly IDocumentRenderer _htmlRenderer;
    private readonly IEpubFidelityAnalyzer _epubFidelityAnalyzer;
    private readonly IHtmlBookPackageRenderer _htmlBookRenderer;
    private readonly EpubCorpusQualificationService _corpusQualificationService;
    private readonly IEpubLargePublicationGate _largePublicationGate;
    private readonly IEpubLargePublicationReviewPackageGenerator _reviewPackageGenerator;
    private readonly IEpubPrivateInventoryService _privateInventoryService;
    private readonly IEpubPrivateQualificationService _privateQualificationService;
    private readonly EpubPrivateDifferenceMatrixService _privateDifferenceMatrixService;
    private readonly IEpubPrivateVisualReviewService _privateVisualReviewService;

    public CliOperations(
        IFlowDocumentSerializer serializer,
        IEpubImporter epubImporter,
        IEpubPublicationInspector epubInspector,
        DocumentValidator validator,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IDocumentRenderer htmlRenderer,
        IEpubFidelityAnalyzer? epubFidelityAnalyzer = null,
        IHtmlBookPackageRenderer? htmlBookRenderer = null,
        EpubCorpusQualificationService? corpusQualificationService = null,
        IEpubLargePublicationGate? largePublicationGate = null,
        IEpubLargePublicationReviewPackageGenerator? reviewPackageGenerator = null,
        IEpubPrivateInventoryService? privateInventoryService = null,
        IEpubPrivateQualificationService? privateQualificationService = null,
        EpubPrivateDifferenceMatrixService? privateDifferenceMatrixService = null,
        IEpubPrivateVisualReviewService? privateVisualReviewService = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(epubImporter);
        ArgumentNullException.ThrowIfNull(epubInspector);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);

        _serializer = serializer;
        _epubImporter = epubImporter;
        _epubInspector = epubInspector;
        _validator = validator;
        _integrityService = integrityService;
        _layoutEngine = layoutEngine;
        _htmlRenderer = htmlRenderer;
        _epubFidelityAnalyzer = epubFidelityAnalyzer ?? new EpubFidelityAnalyzer();
        _htmlBookRenderer = htmlBookRenderer ?? new HtmlBookPackageRenderer();
        _corpusQualificationService = corpusQualificationService ?? new EpubCorpusQualificationService();
        _largePublicationGate = largePublicationGate ?? new EpubLargePublicationGate();
        _reviewPackageGenerator = reviewPackageGenerator ?? new EpubLargePublicationReviewPackageGenerator();
        _privateInventoryService = privateInventoryService ?? new EpubPrivateInventoryService();
        _privateQualificationService = privateQualificationService ?? new EpubPrivateQualificationService();
        _privateDifferenceMatrixService = privateDifferenceMatrixService ?? new EpubPrivateDifferenceMatrixService();
        _privateVisualReviewService = privateVisualReviewService ?? new EpubPrivateVisualReviewService();
    }

    /// <summary>Executes a parsed command and writes its normal output.</summary>
    public async Task<int> ExecuteAsync(
        CliCommand command,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(command, output, error, new CliTextCatalog(), cancellationToken).ConfigureAwait(false);

    /// <summary>Executes a parsed command with the selected human-readable output catalog.</summary>
    public async Task<int> ExecuteAsync(
        CliCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(text);

        return command switch
        {
            HelpCommand help => await ShowHelpAsync(help, output, text).ConfigureAwait(false),
            SampleCommand sample => await CreateSampleAsync(sample, output, text, cancellationToken).ConfigureAwait(false),
            ImportEpubCommand import => await ImportEpubAsync(import, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            InspectEpubCommand inspectEpub => await InspectEpubAsync(inspectEpub, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            InventoryEpubCommand inventory => await InventoryEpubAsync(inventory, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            QualifyEpubInventoryCommand qualification => await QualifyEpubInventoryAsync(
                    qualification,
                    output,
                    error,
                    text,
                cancellationToken)
                .ConfigureAwait(false),
            ClassifyEpubInventoryCommand matrix => await ClassifyEpubInventoryAsync(
                    matrix,
                    output,
                    error,
                    text,
                cancellationToken)
                .ConfigureAwait(false),
            ReviewEpubInventoryCommand inventoryReview => await ReviewEpubInventoryAsync(
                    inventoryReview,
                    output,
                    error,
                    text,
                    cancellationToken)
                .ConfigureAwait(false),
            CorpusCommand corpus => await ExecuteCorpusAsync(corpus, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            QualifyEpubCommand qualify => await QualifyEpubAsync(qualify, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            ReviewEpubCommand review => await ReviewEpubAsync(review, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            ExecutionStatusCommand status => await ExecutionStatusAsync(status, output, error, text, cancellationToken)
                .ConfigureAwait(false),
            ExecutionCleanCommand clean => await ExecutionCleanAsync(clean, output, error, text).ConfigureAwait(false),
            InspectCommand inspect => await InspectAsync(inspect, output, text, cancellationToken).ConfigureAwait(false),
            ValidateCommand validate => await ValidateAsync(validate, output, text, cancellationToken).ConfigureAwait(false),
            HashCommand hash => await HashAsync(hash, output, text, cancellationToken).ConfigureAwait(false),
            RenderHtmlCommand render => await RenderHtmlAsync(render, output, text, cancellationToken).ConfigureAwait(false),
            RenderHtmlBookCommand renderBook => await RenderHtmlBookAsync(renderBook, output, text, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown CLI command."),
        };
    }

    private static async Task<int> ShowHelpAsync(HelpCommand command, TextWriter output, CliTextCatalog text)
    {
        if (command.CommandName is not null)
        {
            if (!CliCommandCatalog.TryGet(command.CommandName, out var descriptor))
            {
                throw new CliOperationException("ErrorUnknownHelpCommand", command.CommandName);
            }

            await CliCommandHelpWriter.WriteAsync(descriptor, output, text).ConfigureAwait(false);
            return 0;
        }

        foreach (var key in new[]
                 {
                     "HelpTitle", "HelpWarning", "HelpGlobalOptions", "HelpLanguage", "HelpBanner", "HelpNoColor",
                     "HelpCommands", "HelpSample", "HelpImport1", "HelpImport2", "HelpImport3", "HelpEpubInspect",
                     "HelpEpubInventory1", "HelpEpubInventory2",
                     "HelpEpubInventoryQualify1", "HelpEpubInventoryQualify2",
                     "HelpEpubInventoryMatrix1", "HelpEpubInventoryMatrix2",
                     "HelpEpubInventoryReview1", "HelpEpubInventoryReview2", "HelpEpubInventoryReview3",
                     "HelpCorpus1", "HelpCorpus2", "HelpEpubQualify1", "HelpEpubQualify2", "HelpEpubQualify3",
                     "HelpEpubReview1", "HelpEpubReview2", "HelpEpubReview3", "HelpOutputPolicy",
                     "HelpExecutionStatus", "HelpExecutionClean",
                     "HelpInspect", "HelpValidate", "HelpHash", "HelpRenderHtml", "HelpRenderBook", "HelpSpecific",
                     "HelpExitCodes",
                 })
        {
            await output.WriteLineAsync(text.Get(key)).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> ExecutionStatusAsync(
        ExecutionStatusCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.DestinationPath));
        var inspection = CliExecutionLock.Inspect(destination);
        var artifacts = CliOutputPolicy.InspectArtifacts(destination);
        var report = new CliExecutionStatusReport(
            inspection,
            artifacts,
            File.Exists(destination),
            Directory.Exists(destination));

        if (command.JsonOutputPath is not null)
        {
            var jsonPath = Path.GetFullPath(command.JsonOutputPath);
            if (PathsEqual(jsonPath, destination) || PathsEqual(jsonPath, inspection.LockPath))
            {
                await error.WriteLineAsync(text.Diagnostic(
                        "FLOWCLI_INVALID_OUTPUT",
                        "ErrorExecutionStatusOutputConflict"))
                    .ConfigureAwait(false);
                return 1;
            }

            var preparation = CliOutputPolicy.PrepareFile(jsonPath, command.Force, resume: command.Force);
            if (!await ReportOutputPreparationAsync(preparation, jsonPath, output, error, text).ConfigureAwait(false))
            {
                return 1;
            }

            await CliExecutionStatusReportJsonSerializer.WriteAtomicallyAsync(report, jsonPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelExecutionStatusJson", jsonPath)).ConfigureAwait(false);
        }

        await output.WriteLineAsync(text.Format(
                "LabelExecutionLockStatus",
                LockStatus(report.Lock, text)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelExecutionStatusId",
                report.Lock.ExecutionId?.ToString("N") ?? text.Get("ValueNone")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelExecutionArtifacts",
                artifacts.TemporaryFiles,
                artifacts.StagingDirectories,
                artifacts.BackupDirectories))
            .ConfigureAwait(false);
        return inspection.IsValid ? 0 : 2;
    }

    private static async Task<int> ExecutionCleanAsync(
        ExecutionCleanCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.DestinationPath));
        var acquisition = CliExecutionLock.AcquireForMaintenance(destination, command.ExecutionId);
        using var executionLock = acquisition.ExecutionLock;
        if (!acquisition.IsSuccess)
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_EXECUTION_CLEAN",
                    acquisition.ErrorResourceKey!,
                    destination))
                .ConfigureAwait(false);
            return 1;
        }

        var cleanup = CliOutputPolicy.CleanupArtifacts(destination);
        if (!cleanup.IsSuccess)
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_EXECUTION_CLEAN",
                    cleanup.ErrorResourceKey!,
                    destination))
                .ConfigureAwait(false);
            return 1;
        }

        executionLock!.Complete();
        await output.WriteLineAsync(text.Format("LabelExecutionCleanId", command.ExecutionId.ToString("N")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelExecutionArtifactsRemoved", cleanup.RemovedArtifacts))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelExecutionBackupRestored",
                text.Get(cleanup.RestoredBackup ? "ValueYes" : "ValueNo")))
            .ConfigureAwait(false);
        return 0;
    }

    private static string LockStatus(CliExecutionInspection inspection, CliTextCatalog text)
    {
        if (!inspection.Exists)
        {
            return text.Get("ExecutionStateMissing");
        }

        if (!inspection.IsValid)
        {
            return text.Get("ExecutionStateInvalid");
        }

        if (inspection.IsWriterActive)
        {
            return text.Get("ExecutionStateActive");
        }

        return inspection.RecordedState switch
        {
            CliExecutionRecordedState.Completed => text.Get("ExecutionStateCompleted"),
            CliExecutionRecordedState.Interrupted => text.Get("ExecutionStateInterrupted"),
            _ => text.Get("ExecutionStateActive"),
        };
    }

    private async Task<int> ExecuteCorpusAsync(
        CorpusCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.GetFullPath(command.ManifestPath);
        var repositoryRoot = Path.GetFullPath(command.RepositoryRoot);
        var reportPath = Path.GetFullPath(command.ReportPath);
        var externalRoot = command.ExternalCorpusRoot is null
            ? null
            : Path.GetFullPath(command.ExternalCorpusRoot);
        var baselinePath = command.AcceptedBaselinePath is null
            ? null
            : Path.GetFullPath(command.AcceptedBaselinePath);
        if (PathsEqual(reportPath, manifestPath)
            || baselinePath is not null && PathsEqual(reportPath, baselinePath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorCorpusReportConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        await using var manifestStream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = new EpubCorpusManifestJsonSerializer().Read(manifestStream);
        if (!read.IsSuccess || read.Manifest is null)
        {
            foreach (var diagnostic in read.Diagnostics)
            {
                var resource = diagnostic.Resource is null ? string.Empty : $" [{diagnostic.Resource}]";
                await error.WriteLineAsync(
                        $"{text.Severity(diagnostic.Severity.ToString())} {diagnostic.Code}{resource}: "
                        + text.DiagnosticMessage(diagnostic.Code, diagnostic.Message))
                    .ConfigureAwait(false);
            }

            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_CORPUS_INVALID", "ErrorCorpusManifestInvalid"))
                .ConfigureAwait(false);
            return 1;
        }

        EpubCorpusBaseline? acceptedBaseline = null;
        if (baselinePath is not null)
        {
            acceptedBaseline = EpubCorpusBaselineJsonSerializer.Deserialize(
                await File.ReadAllBytesAsync(baselinePath, cancellationToken).ConfigureAwait(false));
        }

        var lockAcquisition = CliExecutionLock.Acquire(reportPath, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, reportPath, output, error, text).ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareFile(reportPath, command.Force, command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, reportPath, output, error, text).ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        var options = new EpubCorpusDiscoveryOptions(repositoryRoot, externalRoot);
        var result = await _corpusQualificationService
            .QualifyAsync(read.Manifest, options, acceptedBaseline, cancellationToken)
            .ConfigureAwait(false);
        await EpubCorpusQualificationService
            .WriteLocalDetailedReportAsync(result, reportPath, cancellationToken)
            .ConfigureAwait(false);

        var summary = result.Report.Summary;
        await output.WriteLineAsync(text.Format("LabelCorpusManifest", manifestPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelCorpusReport", reportPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelCorpusTotal", summary.Total)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelCorpusSummary",
                summary.Passed,
                summary.Failed,
                summary.Skipped,
                summary.Inconclusive))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelRepeatedDeterminism",
                text.Get(result.IsDeterministic ? "ValueYes" : "ValueNo")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelBaselineStatus",
                BaselineStatus(result, text)))
            .ConfigureAwait(false);

        var passed = result.IsDeterministic
            && summary.Failed == 0
            && summary.Skipped == 0
            && summary.Inconclusive == 0
            && (result.AcceptedBaselineComparison is null || result.AcceptedBaselineComparison.IsMatch);
        executionLock!.Complete();
        return passed ? 0 : 2;
    }

    private async Task<int> QualifyEpubAsync(
        QualifyEpubCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        var reportPath = Path.GetFullPath(command.ReportPath);
        if (!Path.IsPathFullyQualified(command.RepositoryRoot))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorGateAbsoluteRepositoryRoot"))
                .ConfigureAwait(false);
            return 1;
        }

        var repositoryRoot = Path.GetFullPath(command.RepositoryRoot);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_UNSUPPORTED_INPUT", "ErrorQualifyOnlyEpub"))
                .ConfigureAwait(false);
            return 1;
        }

        if (PathsEqual(sourcePath, reportPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorGateReportSourceConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (IsInside(repositoryRoot, sourcePath) || IsInside(repositoryRoot, reportPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorGateOutsideRepository"))
                .ConfigureAwait(false);
            return 1;
        }

        var lockAcquisition = CliExecutionLock.Acquire(reportPath, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, reportPath, output, error, text).ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareFile(reportPath, command.Force, command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, reportPath, output, error, text).ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        var candidate = new EpubLargePublicationCandidate(
            command.CandidateId,
            sourcePath,
            legalUseDeclared: true,
            drmFreeDeclared: true);
        var options = new EpubLargePublicationGateOptions(
            command.CandidateId,
            command.ExpectedSourceSha256,
            command.RepetitionCount,
            requireHumanReview: true);
        var report = await _largePublicationGate.ExecuteAsync(candidate, options, cancellationToken).ConfigureAwait(false);
        await EpubLargePublicationGateReportJsonSerializer.WriteAtomicallyAsync(
                report,
                reportPath,
                command.IncludeEnvironment,
                cancellationToken)
            .ConfigureAwait(false);

        var failedAutomatic = report.Result.AutomaticChecks.Count(static check =>
            check.Status is not EpubLargePublicationGateStatus.Passed
                and not EpubLargePublicationGateStatus.PassedWithWarnings);
        var warningCount = report.Result.Diagnostics.Count(static diagnostic =>
            diagnostic.Severity == EpubLargePublicationGateDiagnosticSeverity.Warning);
        await output.WriteLineAsync(text.Format("LabelGateCandidate", command.CandidateId.Value)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelGateReport", reportPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelGateStatus", report.Result.Status)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelGateRepetitions", command.RepetitionCount)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelGateAutomaticChecks",
                report.Result.AutomaticChecks.Length - failedAutomatic,
                failedAutomatic,
                warningCount))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Get("GateHumanReviewPending")).ConfigureAwait(false);
        executionLock!.Complete();
        return failedAutomatic == 0 ? 0 : 2;
    }

    private async Task<int> ReviewEpubAsync(
        ReviewEpubCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_UNSUPPORTED_INPUT", "ErrorReviewOnlyEpub"))
                .ConfigureAwait(false);
            return 1;
        }

        if (!Path.IsPathFullyQualified(command.OutputDirectory)
            || !Path.IsPathFullyQualified(command.RepositoryRoot))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorReviewAbsolutePaths"))
                .ConfigureAwait(false);
            return 1;
        }

        var outputDirectory = Path.GetFullPath(command.OutputDirectory);
        var repositoryRoot = Path.GetFullPath(command.RepositoryRoot);
        if (IsInside(repositoryRoot, sourcePath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorReviewSourceOutsideRepository"))
                .ConfigureAwait(false);
            return 1;
        }

        var lockAcquisition = CliExecutionLock.Acquire(outputDirectory, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, outputDirectory, output, error, text)
                .ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareReviewDirectory(
            outputDirectory,
            command.Force,
            command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, outputDirectory, output, error, text)
                .ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        var candidate = new EpubLargePublicationCandidate(
            command.CandidateId,
            sourcePath,
            legalUseDeclared: true,
            drmFreeDeclared: true);
        var options = new EpubLargePublicationReviewOptions(
            command.CandidateId,
            command.ExpectedSourceSha256,
            outputDirectory,
            repositoryRoot,
            command.UiLanguage);
        var result = await _reviewPackageGenerator.GenerateAsync(candidate, options, cancellationToken)
            .ConfigureAwait(false);

        await output.WriteLineAsync(text.Format("LabelReviewCandidate", command.CandidateId.Value)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelReviewDirectory", result.OutputDirectory)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelReviewPage", Path.Combine(result.OutputDirectory, "review.html")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelReviewChecklist",
                Path.Combine(result.OutputDirectory, "review-checklist.json")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelReviewSamples", result.Samples.Length)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelReviewTargets", result.Targets.Length)).ConfigureAwait(false);
        executionLock!.Complete();
        return 0;
    }

    private static async Task<bool> ReportLockAcquisitionAsync(
        CliExecutionLockAcquisition acquisition,
        string path,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text)
    {
        if (!acquisition.IsSuccess)
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_EXECUTION_LOCK",
                    acquisition.ErrorResourceKey!,
                    path))
                .ConfigureAwait(false);
            return false;
        }

        await output.WriteLineAsync(text.Format(
                "LabelExecutionStarted",
                acquisition.ExecutionLock!.ExecutionId.ToString("N")))
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> ReportOutputPreparationAsync(
        CliOutputPreparation preparation,
        string path,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text)
    {
        if (!preparation.IsSuccess)
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_OUTPUT_POLICY",
                    preparation.ErrorResourceKey!,
                    path))
                .ConfigureAwait(false);
            return false;
        }

        if (preparation.Resumed)
        {
            await output.WriteLineAsync(text.Format("LabelInterruptedRunRecovered", path)).ConfigureAwait(false);
        }

        return true;
    }

    private static string BaselineStatus(EpubCorpusQualificationResult result, CliTextCatalog text) =>
        result.AcceptedBaselineComparison is null
            ? text.Get("ValueNotProvided")
            : text.Get(result.AcceptedBaselineComparison.IsMatch ? "ValueMatched" : "ValueMismatched");

    private async Task<int> InspectEpubAsync(
        InspectEpubCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_UNSUPPORTED_INPUT", "ErrorInspectOnlyEpub"))
                .ConfigureAwait(false);
            return 1;
        }

        var jsonOutputPath = command.JsonOutputPath is null ? null : Path.GetFullPath(command.JsonOutputPath);
        if (jsonOutputPath is not null && PathsEqual(sourcePath, jsonOutputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorJsonSourceConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await _epubInspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
        await WriteEpubInspectionAsync(inspection, sourcePath, output, text).ConfigureAwait(false);
        await WriteEpubDiagnosticsAsync(
                inspection.Diagnostics,
                output,
                error,
                text,
                detailsPersisted: command.JsonOutputPath is not null)
            .ConfigureAwait(false);

        if (jsonOutputPath is not null)
        {
            EnsureParentDirectory(jsonOutputPath);
            await WriteInspectionAtomicallyAsync(inspection, jsonOutputPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelReportJson", jsonOutputPath)).ConfigureAwait(false);
        }

        return inspection.IsSuccess ? 0 : 1;
    }

    private async Task<int> InventoryEpubAsync(
        InventoryEpubCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.SourceDirectory));
        var outputPath = Path.GetFullPath(command.OutputPath);
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.RepositoryRoot));
        if (IsInside(repositoryRoot, sourceDirectory) || IsInside(repositoryRoot, outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryRepositoryPath"))
                .ConfigureAwait(false);
            return 1;
        }

        if (IsInside(sourceDirectory, outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventorySourceOutput"))
                .ConfigureAwait(false);
            return 1;
        }

        if (File.Exists(outputPath))
        {
            if (!command.Force)
            {
                await error.WriteLineAsync(text.Diagnostic("FLOWCLI_OUTPUT_EXISTS", "ErrorInventoryOutputExists"))
                    .ConfigureAwait(false);
                return 1;
            }

            if (!IsRecognizedInventory(outputPath))
            {
                await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryOutputUnrecognized"))
                    .ConfigureAwait(false);
                return 1;
            }
        }

        var report = await _privateInventoryService.InventoryAsync(sourceDirectory, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await EpubPrivateInventoryReportJsonSerializer.WriteAtomicallyAsync(report, outputPath, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelInventoryReport", outputPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelInventoryFiles",
                report.Summary.DiscoveredFiles.ToString(CultureInfo.InvariantCulture),
                report.Summary.DistinctPublications.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelInventorySummary",
                report.Summary.Ready.ToString(CultureInfo.InvariantCulture),
                report.Summary.ReviewRequired.ToString(CultureInfo.InvariantCulture),
                report.Summary.Protected.ToString(CultureInfo.InvariantCulture),
                report.Summary.Corrupt.ToString(CultureInfo.InvariantCulture),
                report.Summary.Unsuitable.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        return report.Diagnostics.Any(static item => item.Severity == EpubPrivateInventoryDiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static bool IsRecognizedInventory(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("format", out var format)
                   && format.GetString() == EpubPrivateInventoryReport.CurrentFormat;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<int> QualifyEpubInventoryAsync(
        QualifyEpubInventoryCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(command.RepositoryRoot)
            || !Path.IsPathFullyQualified(command.ReportPath))
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_INVALID_OUTPUT",
                    "ErrorInventoryQualificationAbsolutePaths"))
                .ConfigureAwait(false);
            return 1;
        }

        var sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.SourceDirectory));
        var reportPath = Path.GetFullPath(command.ReportPath);
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.RepositoryRoot));
        if (IsInside(repositoryRoot, sourceDirectory) || IsInside(repositoryRoot, reportPath))
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_INVALID_OUTPUT",
                    "ErrorInventoryQualificationRepositoryPath"))
                .ConfigureAwait(false);
            return 1;
        }

        if (IsInside(sourceDirectory, reportPath))
        {
            await error.WriteLineAsync(text.Diagnostic(
                    "FLOWCLI_INVALID_OUTPUT",
                    "ErrorInventoryQualificationSourceOutput"))
                .ConfigureAwait(false);
            return 1;
        }

        var lockAcquisition = CliExecutionLock.Acquire(reportPath, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, reportPath, output, error, text).ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareFile(reportPath, command.Force, command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, reportPath, output, error, text)
                .ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        var report = await _privateQualificationService.QualifyAsync(
                sourceDirectory,
                repositoryRoot,
                new EpubPrivateQualificationOptions(legalUseDeclared: true, drmFreeDeclared: true),
                cancellationToken)
            .ConfigureAwait(false);
        await EpubPrivateQualificationReportJsonSerializer
            .WriteAtomicallyAsync(report, reportPath, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelInventoryQualificationReport", reportPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelInventoryQualificationSummary",
                report.Summary.Total,
                report.Summary.Eligible,
                report.Summary.Passed,
                report.Summary.Failed,
                report.Summary.Inconclusive,
                report.Summary.Nondeterministic,
                report.Summary.Skipped))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelRepeatedDeterminism",
                text.Get(report.DeterministicAcrossRepeatedRuns ? "ValueYes" : "ValueNo")))
            .ConfigureAwait(false);
        executionLock!.Complete();
        return report.DeterministicAcrossRepeatedRuns
               && report.Summary.Failed == 0
               && report.Summary.Inconclusive == 0
               && report.Summary.Nondeterministic == 0
               && report.Summary.Skipped == 0
            ? 0
            : 2;
    }

    private async Task<int> ClassifyEpubInventoryAsync(
        ClassifyEpubInventoryCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        const long maximumReportBytes = 64L * 1024 * 1024;
        if (!Path.IsPathFullyQualified(command.QualificationReportPath)
            || !Path.IsPathFullyQualified(command.OutputPath)
            || !Path.IsPathFullyQualified(command.RepositoryRoot))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryMatrixAbsolutePaths"))
                .ConfigureAwait(false);
            return 1;
        }

        var qualificationPath = Path.GetFullPath(command.QualificationReportPath);
        var outputPath = Path.GetFullPath(command.OutputPath);
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.RepositoryRoot));
        if (IsInside(repositoryRoot, qualificationPath) || IsInside(repositoryRoot, outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryMatrixRepositoryPath"))
                .ConfigureAwait(false);
            return 1;
        }

        if (PathsEqual(qualificationPath, outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryMatrixInputConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        var input = new FileInfo(qualificationPath);
        if (!input.Exists || input.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_INPUT", "ErrorInventoryMatrixUnsafeInput"))
                .ConfigureAwait(false);
            return 1;
        }

        if (input.Length > maximumReportBytes)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_INPUT", "ErrorInventoryMatrixInputLimit"))
                .ConfigureAwait(false);
            return 1;
        }

        if (File.Exists(outputPath) && command.Force && !IsRecognizedDifferenceMatrix(outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryMatrixOutputUnrecognized"))
                .ConfigureAwait(false);
            return 1;
        }

        var bytes = await File.ReadAllBytesAsync(qualificationPath, cancellationToken).ConfigureAwait(false);
        var actualHash = new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes)));
        if (actualHash != command.ExpectedQualificationSha256)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_HASH_MISMATCH", "ErrorInventoryMatrixHashMismatch"))
                .ConfigureAwait(false);
            return 1;
        }

        var qualification = EpubPrivateQualificationReportJsonSerializer.Deserialize(bytes);
        var matrix = _privateDifferenceMatrixService.Create(qualification, actualHash);
        var lockAcquisition = CliExecutionLock.Acquire(outputPath, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, outputPath, output, error, text).ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareFile(outputPath, command.Force, command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, outputPath, output, error, text)
                .ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        await EpubPrivateDifferenceMatrixJsonSerializer.WriteAtomicallyAsync(matrix, outputPath, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelInventoryMatrixReport", outputPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelInventoryMatrixSummary",
                matrix.Summary.TotalCandidates,
                matrix.Summary.Approved,
                matrix.Summary.ApprovedWithApproximations,
                matrix.Summary.UnsupportedContent,
                matrix.Summary.ContentLoss,
                matrix.Summary.BrokenSourceReference,
                matrix.Summary.FlowError,
                matrix.Summary.HumanReviewRequired))
            .ConfigureAwait(false);
        executionLock!.Complete();
        return matrix.Summary.ContentLoss == 0 && matrix.Summary.FlowError == 0 ? 0 : 2;
    }

    private static bool IsRecognizedDifferenceMatrix(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("format", out var format)
                   && format.GetString() == EpubPrivateDifferenceMatrix.CurrentFormat;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<int> ReviewEpubInventoryAsync(
        ReviewEpubInventoryCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        const long maximumReportBytes = 64L * 1024 * 1024;
        if (!Path.IsPathFullyQualified(command.SourceDirectory)
            || !Path.IsPathFullyQualified(command.QualificationReportPath)
            || !Path.IsPathFullyQualified(command.OutputDirectory)
            || !Path.IsPathFullyQualified(command.RepositoryRoot))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryReviewAbsolutePaths"))
                .ConfigureAwait(false);
            return 1;
        }

        var sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.SourceDirectory));
        var qualificationPath = Path.GetFullPath(command.QualificationReportPath);
        var outputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.OutputDirectory));
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.RepositoryRoot));
        if (IsInside(repositoryRoot, sourceDirectory)
            || IsInside(repositoryRoot, qualificationPath)
            || IsInside(repositoryRoot, outputDirectory))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryReviewRepositoryPath"))
                .ConfigureAwait(false);
            return 1;
        }

        if (IsInside(sourceDirectory, qualificationPath) || IsInside(outputDirectory, qualificationPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorInventoryReviewInputConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        var input = new FileInfo(qualificationPath);
        if (!input.Exists
            || input.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || input.Length > maximumReportBytes)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_INPUT", "ErrorInventoryReviewUnsafeInput"))
                .ConfigureAwait(false);
            return 1;
        }

        var bytes = await File.ReadAllBytesAsync(qualificationPath, cancellationToken).ConfigureAwait(false);
        var actualHash = new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes)));
        if (actualHash != command.ExpectedQualificationSha256)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_HASH_MISMATCH", "ErrorInventoryReviewHashMismatch"))
                .ConfigureAwait(false);
            return 1;
        }

        EpubPrivateQualificationReport qualification;
        try
        {
            qualification = EpubPrivateQualificationReportJsonSerializer.Deserialize(bytes);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_INPUT", "ErrorInventoryReviewUnsafeInput"))
                .ConfigureAwait(false);
            return 1;
        }

        var lockAcquisition = CliExecutionLock.Acquire(outputDirectory, command.Resume);
        using var executionLock = lockAcquisition.ExecutionLock;
        if (!await ReportLockAcquisitionAsync(lockAcquisition, outputDirectory, output, error, text)
                .ConfigureAwait(false))
        {
            return 1;
        }

        var outputPreparation = CliOutputPolicy.PrepareReviewDirectory(outputDirectory, command.Force, command.Resume);
        if (!await ReportOutputPreparationAsync(outputPreparation, outputDirectory, output, error, text)
                .ConfigureAwait(false))
        {
            executionLock!.Complete();
            return 1;
        }

        var result = await _privateVisualReviewService.GenerateAsync(
                qualification,
                actualHash,
                new EpubPrivateVisualReviewOptions(
                    sourceDirectory,
                    outputDirectory,
                    repositoryRoot,
                    legalUseDeclared: true,
                    drmFreeDeclared: true,
                    command.UiLanguage),
                cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelInventoryReviewDirectory", result.OutputDirectory))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelInventoryReviewIndex", Path.Combine(result.OutputDirectory, "index.html")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelInventoryReviewSummary",
                result.Report.Summary.Total,
                result.Report.Summary.Generated,
                result.Report.Summary.Missing,
                result.Report.Summary.Skipped,
                result.Report.Summary.Failed))
            .ConfigureAwait(false);
        executionLock!.Complete();
        return result.Report.Summary.Generated == result.Report.Summary.Total ? 0 : 2;
    }

    private static async Task WriteEpubInspectionAsync(
        EpubPublicationInspection inspection,
        string sourcePath,
        TextWriter output,
        CliTextCatalog text)
    {
        var package = inspection.Package;
        var none = text.Get("ValueNone");
        await output.WriteLineAsync(text.Format("LabelInspection", sourcePath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelStatus", text.Get(inspection.IsSuccess ? "ValueValid" : "ValueInvalid")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelContainer", inspection.ContainerPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelPackage", package?.Path ?? text.Get("ValueUnavailable")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelEpubVersion",
                package?.VersionFamily.ToString() ?? text.Get("ValueUnknown"),
                package?.DeclaredVersion ?? text.Get("ValueUnknown")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelTitle", package?.Title ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelIdentifier", package?.Identifier ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelLanguage", package?.Language ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelCreators", string.Join(", ", package?.Creators ?? [])))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelManifestItems",
                inspection.Resources.ManifestItemCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelSpineItems",
                inspection.Spine.Length.ToString(CultureInfo.InvariantCulture),
                inspection.Spine.Count(static item => item.IsLinear).ToString(CultureInfo.InvariantCulture),
                inspection.Spine.Count(static item => !item.IsLinear).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelNavigationDocuments",
                inspection.NavigationDocumentPaths.Length.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelArchiveEntries",
                inspection.Resources.ArchiveEntryCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelCompressedBytes",
                inspection.Resources.TotalCompressedBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelUncompressedBytes",
                inspection.Resources.TotalUncompressedBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Get("LabelResourceTypes")).ConfigureAwait(false);
        foreach (var (mediaType, count) in inspection.Resources.MediaTypeCounts)
        {
            await output.WriteLineAsync($"  {mediaType}: {count.ToString(CultureInfo.InvariantCulture)}")
                .ConfigureAwait(false);
        }
    }

    private async Task<int> ImportEpubAsync(
        ImportEpubCommand command,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(command.SourcePath);
        var outputPath = command.OutputPath is null ? null : Path.GetFullPath(command.OutputPath);
        var diagnosticsPath = command.DiagnosticsJsonOutputPath is null
            ? null
            : Path.GetFullPath(command.DiagnosticsJsonOutputPath);
        var fidelityPath = command.FidelityReportOutputPath is null
            ? null
            : Path.GetFullPath(command.FidelityReportOutputPath);
        var metadataPath = command.MetadataJsonOutputPath is null
            ? null
            : Path.GetFullPath(command.MetadataJsonOutputPath);
        var processingPath = command.ProcessingJsonOutputPath is null
            ? null
            : Path.GetFullPath(command.ProcessingJsonOutputPath);
        var sourceMapPath = command.SourceMapJsonOutputPath is null
            ? null
            : Path.GetFullPath(command.SourceMapJsonOutputPath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_UNSUPPORTED_INPUT", "ErrorImportOnlyEpub"))
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is not null && PathsEqual(sourcePath, outputPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorOutputSourceConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (diagnosticsPath is not null && PathsEqual(sourcePath, diagnosticsPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorDiagnosticSourceConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null && PathsEqual(sourcePath, fidelityPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorFidelitySourceConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is not null && diagnosticsPath is not null && PathsEqual(outputPath, diagnosticsPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorDocumentDiagnosticsConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null
            && ((outputPath is not null && PathsEqual(outputPath, fidelityPath))
                || (diagnosticsPath is not null && PathsEqual(diagnosticsPath, fidelityPath))))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorFidelityOutputConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (!await ValidateEvidenceOutputPathsAsync(
                sourcePath,
                outputPath,
                diagnosticsPath,
                fidelityPath,
                metadataPath,
                processingPath,
                sourceMapPath,
                error,
                text).ConfigureAwait(false))
        {
            return 1;
        }

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var import = await _epubImporter.ImportAsync(source, progress: null, cancellationToken).ConfigureAwait(false);
        var pipelineMetrics = import.Metrics;

        if (import.Document is not null && outputPath is null)
        {
            outputPath = Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory(),
                PortableBookFileName.FromTitle(import.Document.Metadata.Title, sourcePath));
        }

        if (outputPath is not null && diagnosticsPath is not null && PathsEqual(outputPath, diagnosticsPath))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorDocumentDiagnosticsConflict"))
                .ConfigureAwait(false);
            return 1;
        }

        if (fidelityPath is not null
            && ((outputPath is not null && PathsEqual(outputPath, fidelityPath))
                || (diagnosticsPath is not null && PathsEqual(diagnosticsPath, fidelityPath))))
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_INVALID_OUTPUT", "ErrorFidelityOutputConflict"))
                .ConfigureAwait(false);
            return 1;
        }


        if (!await ValidateEvidenceOutputPathsAsync(
                sourcePath,
                outputPath,
                diagnosticsPath,
                fidelityPath,
                metadataPath,
                processingPath,
                sourceMapPath,
                error,
                text).ConfigureAwait(false))
        {
            return 1;
        }

        if (diagnosticsPath is not null)
        {
            EnsureParentDirectory(diagnosticsPath);
            await WriteImportDiagnosticsAtomicallyAsync(import, diagnosticsPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelDiagnosticsJson", diagnosticsPath)).ConfigureAwait(false);
        }

        if (fidelityPath is not null)
        {
            var fidelityStarted = Stopwatch.GetTimestamp();
            var fidelity = _epubFidelityAnalyzer.Analyze(import, cancellationToken);
            pipelineMetrics = pipelineMetrics?.AddPhaseTiming(
                EpubImportPhase.AnalyzingFidelity,
                Stopwatch.GetElapsedTime(fidelityStarted));
            EnsureParentDirectory(fidelityPath);
            await WriteFidelityReportAtomicallyAsync(fidelity, fidelityPath, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelFidelityReport", fidelityPath)).ConfigureAwait(false);
        }

        await WriteImportEvidenceSidecarsAsync(
                import,
                metadataPath,
                processingPath,
                sourceMapPath,
                output,
                text,
                cancellationToken)
            .ConfigureAwait(false);

        await WriteEpubDiagnosticsAsync(
                import.Diagnostics,
                output,
                error,
                text,
                detailsPersisted: diagnosticsPath is not null)
            .ConfigureAwait(false);

        if (!import.IsSuccess || import.Document is null)
        {
            await WriteImportMetricsAsync(pipelineMetrics, output, text).ConfigureAwait(false);
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_EPUB_IMPORT_FAILED", "ErrorImportFailed"))
                .ConfigureAwait(false);
            return 1;
        }

        if (outputPath is null)
        {
            throw new CliOperationException("ErrorImportOutputMissing");
        }

        var validation = _validator.Validate(import.Document);
        if (!validation.IsValid)
        {
            foreach (var diagnostic in validation.Diagnostics)
            {
                var location = diagnostic.NodeId is null ? string.Empty : $" [{diagnostic.NodeId}]";
                await error.WriteLineAsync(
                        $"{text.Severity(diagnostic.Severity.ToString())} {diagnostic.Code}{location}: "
                        + text.DiagnosticMessage(diagnostic.Code, diagnostic.Message))
                    .ConfigureAwait(false);
            }

            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_EPUB_DOCUMENT_INVALID", "ErrorImportedDocumentInvalid"))
                .ConfigureAwait(false);
            return 2;
        }

        EnsureParentDirectory(outputPath);
        var serializationStarted = Stopwatch.GetTimestamp();
        await WriteDocumentAtomicallyAsync(import.Document, outputPath, cancellationToken).ConfigureAwait(false);
        pipelineMetrics = pipelineMetrics?
            .AddPhaseTiming(EpubImportPhase.SerializingDocument, Stopwatch.GetElapsedTime(serializationStarted))
            .WithOutputSizes(new FileInfo(outputPath).Length, null, null);

        var hash = _integrityService.ComputeHash(import.Document);
        await output.WriteLineAsync(text.Format("LabelImportedEpub", sourcePath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelFlowDocument", outputPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelTitle", import.Document.Metadata.Title)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelDocumentId", import.Document.Identity.Id)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelNodes",
                import.Document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelAssets",
                import.Document.Assets.Count.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await WriteHashAsync(output, hash, text).ConfigureAwait(false);
        await WriteImportMetricsAsync(pipelineMetrics, output, text).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> CreateSampleAsync(
        SampleCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(command.OutputPath);
        EnsureParentDirectory(path);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await _serializer.SerializeAsync(SampleBookFactory.Create(), stream, cancellationToken).ConfigureAwait(false);
        }

        await output.WriteLineAsync(text.Format("LabelSampleWritten", path)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> InspectAsync(
        InspectCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var none = text.Get("ValueNone");
        await output.WriteLineAsync(text.Format("LabelId", document.Identity.Id)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelVersion", document.Identity.Version ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelTitle", document.Metadata.Title)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelSubtitle", document.Metadata.Subtitle ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelLanguage", document.Metadata.Language ?? none)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelAuthors", string.Join(", ", document.Metadata.Authors))).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelNodes", document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelChapters", document.Content.Children.Count(static node => node is Chapter).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelSections", CountNodes<Section>(document).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelParagraphs", CountNodes<Paragraph>(document).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelFigures", CountNodes<Figure>(document).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelFootnotes", CountNodes<Footnote>(document).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelAssets", document.Assets.Count.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelAnchors", document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelPresentation", text.Get(document.Presentation is null ? "ValueNo" : "ValueYes")))
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ValidateAsync(
        ValidateCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var validation = _validator.Validate(document);
        if (validation.IsValid)
        {
            await output.WriteLineAsync(text.Get("ValidationValid")).ConfigureAwait(false);
            return 0;
        }

        foreach (var diagnostic in validation.Diagnostics)
        {
            var location = diagnostic.NodeId is null ? string.Empty : $" [{diagnostic.NodeId}]";
            await output.WriteLineAsync(
                    $"{text.Severity(diagnostic.Severity.ToString())} {diagnostic.Code}{location}: "
                    + text.DiagnosticMessage(diagnostic.Code, diagnostic.Message))
                .ConfigureAwait(false);
        }

        return 2;
    }

    private async Task<int> HashAsync(
        HashCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var hash = _integrityService.ComputeHash(document);
        await WriteHashAsync(output, hash, text).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RenderHtmlAsync(
        RenderHtmlCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(command.DocumentPath, cancellationToken).ConfigureAwait(false);
        var preferences = new UserReadingPreferences();
        var context = new LayoutContext(
            command.ViewportWidth,
            command.ViewportHeight,
            GetDeviceClass(command.ViewportWidth),
            ReadingMode.Flow,
            userPreferences: preferences);
        var layout = _layoutEngine.Layout(document, context);
        var rendered = _htmlRenderer.Render(document, layout, preferences);
        var outputPath = Path.GetFullPath(command.OutputPath);
        EnsureParentDirectory(outputPath);
        rendered.WriteTo(outputPath);

        var hash = _integrityService.ComputeHash(document);
        await output.WriteLineAsync(text.Format("LabelRendered", outputPath)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelDocumentId", document.Identity.Id)).ConfigureAwait(false);
        await WriteHashAsync(output, hash, text).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelAnchors", document.Index.NodeCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelViewport",
                CssNumber(command.ViewportWidth),
                CssNumber(command.ViewportHeight),
                layout.Profile.ViewportCategory))
            .ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RenderHtmlBookAsync(
        RenderHtmlBookCommand command,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        const double width = 1024;
        const double height = 768;
        var documentPath = Path.GetFullPath(command.DocumentPath);
        var outputDirectory = Path.GetFullPath(command.OutputDirectory);
        ValidateHtmlBookOutputPath(documentPath, outputDirectory);

        var document = await ReadDocumentAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var preferences = new UserReadingPreferences();
        var layout = _layoutEngine.Layout(
            document,
            new LayoutContext(width, height, GetDeviceClass(width), ReadingMode.Flow, userPreferences: preferences));
        var hash = _integrityService.ComputeHash(document);
        var renderingStarted = Stopwatch.GetTimestamp();
        var package = _htmlBookRenderer.Render(
            document,
            layout,
            preferences,
            new HtmlBookIntegrity(hash.Algorithm, hash.Hash, hash.CanonicalizationVersion),
            new HtmlBookPackageOptions(command.UiLanguage),
            cancellationToken);
        var renderingDuration = Stopwatch.GetElapsedTime(renderingStarted);

        var writingStarted = Stopwatch.GetTimestamp();
        await WriteHtmlBookPackageAtomicallyAsync(package, outputDirectory, cancellationToken).ConfigureAwait(false);
        var writingDuration = Stopwatch.GetElapsedTime(writingStarted);
        await output.WriteLineAsync(text.Format("LabelHtmlBook", outputDirectory)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelEntry", Path.Combine(outputDirectory, "index.html")))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelUiLanguage", UiLanguageName(command.UiLanguage, document.Metadata.Language)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelDocumentId", document.Identity.Id)).ConfigureAwait(false);
        await WriteHashAsync(output, hash, text).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelFiles",
                package.Files.Length.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelChapters",
                package.Files.Count(static file => file.Path.StartsWith("chapters/", StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "LabelHtmlBytes",
                package.Files.Sum(static file => (long)file.Content.Length).ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelRenderDuration", Milliseconds(renderingDuration)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelWriteDuration", Milliseconds(writingDuration)))
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task WriteImportMetricsAsync(
        EpubImportMetrics? metrics,
        TextWriter output,
        CliTextCatalog text)
    {
        if (metrics is null)
        {
            return;
        }

        await output.WriteLineAsync(text.Get("MetricsTitle")).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsTotalDuration", Milliseconds(metrics.TotalDuration))).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsArchiveEntries", metrics.ArchiveEntryCount.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsCompressedBytes", metrics.CompressedBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsUncompressedBytes", metrics.UncompressedBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsAssetBytes", metrics.AssetBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsSpineDocuments", metrics.SpineDocumentsProcessed.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsNodes", metrics.NodesProduced.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("MetricsCharacters", metrics.CharactersProduced.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "MetricsPeakManagedBytes",
                metrics.ApproximatePeakManagedBytes.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        if (metrics.FlowJsonBytes is not null)
        {
            await output.WriteLineAsync(text.Format("MetricsFlowJsonBytes", metrics.FlowJsonBytes.Value.ToString(CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);
        }

        foreach (var timing in metrics.PhaseTimings)
        {
            await output.WriteLineAsync(text.Format("MetricsPhase", timing.Phase, Milliseconds(timing.Duration)))
                .ConfigureAwait(false);
        }
    }

    private static string Milliseconds(TimeSpan value) => value.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UiLanguageName(HtmlBookUiLanguage language, string? publicationLanguage)
    {
        if (language != HtmlBookUiLanguage.Automatic)
        {
            return language switch
            {
                HtmlBookUiLanguage.English => "en",
                HtmlBookUiLanguage.PortuguesePortugal => "pt-PT",
                HtmlBookUiLanguage.PortugueseBrazil => "pt-BR",
                _ => throw new ArgumentOutOfRangeException(nameof(language)),
            };
        }

        var normalized = publicationLanguage?.Replace('_', '-');
        if (normalized is not null
            && (normalized.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("pt-BR-", StringComparison.OrdinalIgnoreCase)))
        {
            return "pt-BR (automatic)";
        }

        return normalized is not null
               && (normalized.Equals("pt", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("pt-", StringComparison.OrdinalIgnoreCase))
            ? "pt-PT (automatic)"
            : "en (automatic fallback)";
    }

    private async Task<FlowDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDocumentAtomicallyAsync(
        FlowDocument document,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await _serializer.SerializeAsync(document, destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteInspectionAtomicallyAsync(
        EpubPublicationInspection inspection,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubInspectionJsonWriter.WriteAsync(inspection, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteImportDiagnosticsAtomicallyAsync(
        EpubImportResult import,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubImportDiagnosticsJsonWriter.WriteAsync(import, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteFidelityReportAtomicallyAsync(
        EpubFidelityReport report,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await EpubFidelityJsonWriter.WriteAsync(report, destination, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteImportEvidenceSidecarsAsync(
        EpubImportResult import,
        string? metadataPath,
        string? processingPath,
        string? sourceMapPath,
        TextWriter output,
        CliTextCatalog text,
        CancellationToken cancellationToken)
    {
        if (metadataPath is not null)
        {
            EnsureParentDirectory(metadataPath);
            await WriteImportEvidenceAtomicallyAsync(
                    import,
                    metadataPath,
                    EpubImportEvidenceJsonWriter.WriteMetadataAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelMetadataJson", metadataPath)).ConfigureAwait(false);
        }

        if (processingPath is not null)
        {
            EnsureParentDirectory(processingPath);
            await WriteImportEvidenceAtomicallyAsync(
                    import,
                    processingPath,
                    EpubImportEvidenceJsonWriter.WriteProcessingAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelProcessingJson", processingPath)).ConfigureAwait(false);
        }

        if (sourceMapPath is not null)
        {
            EnsureParentDirectory(sourceMapPath);
            await WriteImportEvidenceAtomicallyAsync(
                    import,
                    sourceMapPath,
                    EpubImportEvidenceJsonWriter.WriteSourceMapAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(text.Format("LabelSourceMapJson", sourceMapPath)).ConfigureAwait(false);
        }
    }

    private static async Task WriteImportEvidenceAtomicallyAsync(
        EpubImportResult import,
        string outputPath,
        Func<EpubImportResult, Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await write(import, destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<bool> ValidateEvidenceOutputPathsAsync(
        string sourcePath,
        string? documentPath,
        string? diagnosticsPath,
        string? fidelityPath,
        string? metadataPath,
        string? processingPath,
        string? sourceMapPath,
        TextWriter error,
        CliTextCatalog text)
    {
        var evidenceOutputs = new (string Option, string? Path)[]
        {
            ("--metadata-json", metadataPath),
            ("--processing-json", processingPath),
            ("--source-map-json", sourceMapPath),
        };
        foreach (var item in evidenceOutputs)
        {
            if (item.Path is not null && PathsEqual(sourcePath, item.Path))
            {
                await error.WriteLineAsync(text.Diagnostic(
                        "FLOWCLI_INVALID_OUTPUT",
                        "ErrorEvidenceSourceConflict",
                        item.Option))
                    .ConfigureAwait(false);
                return false;
            }
        }

        var outputs = new (string Option, string? Path)[]
        {
            ("--output", documentPath),
            ("--diagnostics-json", diagnosticsPath),
            ("--fidelity-report", fidelityPath),
            ("--metadata-json", metadataPath),
            ("--processing-json", processingPath),
            ("--source-map-json", sourceMapPath),
        };
        for (var left = 0; left < outputs.Length; left++)
        {
            if (outputs[left].Path is null)
            {
                continue;
            }

            for (var right = left + 1; right < outputs.Length; right++)
            {
                if (outputs[right].Path is not null && PathsEqual(outputs[left].Path!, outputs[right].Path!))
                {
                    await error.WriteLineAsync(text.Diagnostic(
                            "FLOWCLI_INVALID_OUTPUT",
                            "ErrorOutputConflict",
                            outputs[left].Option,
                            outputs[right].Option))
                        .ConfigureAwait(false);
                    return false;
                }
            }
        }

        return true;
    }

    private static async Task WriteHtmlBookPackageAtomicallyAsync(
        HtmlBookPackage package,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(outputDirectory)
            ?? throw new CliOperationException("ErrorHtmlBookParentRequired");
        Directory.CreateDirectory(parent);
        if (Directory.Exists(outputDirectory))
        {
            ValidateReplaceableHtmlBookDirectory(outputDirectory);
        }

        var name = Path.GetFileName(outputDirectory);
        var temporaryDirectory = Path.Combine(parent, $".{name}.flow-html-book-{Guid.NewGuid():N}.tmp");
        var backupDirectory = Path.Combine(parent, $".{name}.flow-html-book-{Guid.NewGuid():N}.backup");
        Directory.CreateDirectory(temporaryDirectory);
        var destinationReplaced = false;
        try
        {
            foreach (var file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolvePackageOutputPath(temporaryDirectory, file.Path);
                var targetParent = Path.GetDirectoryName(target);
                if (targetParent is not null)
                {
                    Directory.CreateDirectory(targetParent);
                }

                await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await stream.WriteAsync(file.Content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(outputDirectory))
            {
                Directory.Move(temporaryDirectory, outputDirectory);
                destinationReplaced = true;
                return;
            }

            Directory.Move(outputDirectory, backupDirectory);
            try
            {
                Directory.Move(temporaryDirectory, outputDirectory);
                destinationReplaced = true;
            }
            catch
            {
                if (!Directory.Exists(outputDirectory) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, outputDirectory);
                }

                throw;
            }

            Directory.Delete(backupDirectory, recursive: true);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            if (destinationReplaced && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private static void ValidateHtmlBookOutputPath(string documentPath, string outputDirectory)
    {
        var root = Path.GetPathRoot(outputDirectory);
        if (root is not null
            && PathsEqual(
                Path.TrimEndingDirectorySeparator(outputDirectory),
                Path.TrimEndingDirectorySeparator(root)))
        {
            throw new CliOperationException("ErrorHtmlBookFilesystemRoot");
        }

        if (File.Exists(outputDirectory))
        {
            throw new CliOperationException("ErrorHtmlBookExistingFile");
        }

        var outputPrefix = Path.TrimEndingDirectorySeparator(outputDirectory) + Path.DirectorySeparatorChar;
        if (documentPath.StartsWith(
                outputPrefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new CliOperationException("ErrorHtmlBookContainsSource");
        }

        if (Directory.Exists(outputDirectory)
            && (File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new CliOperationException("ErrorHtmlBookReparsePoint");
        }
    }

    private static void ValidateReplaceableHtmlBookDirectory(string outputDirectory)
    {
        if (ContainsReparsePoint(outputDirectory))
        {
            throw new CliOperationException("ErrorHtmlBookExistingReparsePoint");
        }

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new CliOperationException("ErrorHtmlBookNotReplaceable");
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            if (manifest.RootElement.GetProperty("format").GetString() != HtmlBookPackage.Format)
            {
                throw new CliOperationException("ErrorHtmlBookNotReplaceable");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new CliOperationException("ErrorHtmlBookInvalidManifest", exception);
        }
    }

    private static bool ContainsReparsePoint(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop(), "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
            }
        }

        return false;
    }

    private static string ResolvePackageOutputPath(string root, string packagePath)
    {
        var target = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(
                prefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new CliOperationException("ErrorHtmlBookUnsafePackagePath", packagePath);
        }

        return target;
    }

    private static async Task WriteEpubDiagnosticsAsync(
        IEnumerable<EpubDiagnostic> diagnostics,
        TextWriter output,
        TextWriter error,
        CliTextCatalog text,
        bool detailsPersisted = false)
    {
        const int maximumPersistedDetailsOnConsole = 40;
        var items = diagnostics.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var information = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Information);
        var warnings = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Warning);
        var errors = items.Count(static item => item.Severity == EpubDiagnosticSeverity.Error);
        await output.WriteLineAsync(text.Format(
                "DiagnosticsSummary",
                information.ToString(CultureInfo.InvariantCulture),
                warnings.ToString(CultureInfo.InvariantCulture),
                errors.ToString(CultureInfo.InvariantCulture)))
            .ConfigureAwait(false);
        await output.WriteLineAsync(text.Format(
                "DiagnosticsCodes",
                string.Join(
                    ", ",
                    items.GroupBy(static item => item.Code, StringComparer.Ordinal)
                        .OrderBy(static group => group.Key, StringComparer.Ordinal)
                        .Select(static group => $"{group.Key}={group.Count().ToString(CultureInfo.InvariantCulture)}"))))
            .ConfigureAwait(false);

        var visible = items.AsEnumerable();
        if (detailsPersisted && items.Length > maximumPersistedDetailsOnConsole)
        {
            var errorItems = items.Where(static item => item.Severity == EpubDiagnosticSeverity.Error).ToArray();
            visible = errorItems.Concat(items
                .Where(static item => item.Severity != EpubDiagnosticSeverity.Error)
                .Take(Math.Max(0, maximumPersistedDetailsOnConsole - errorItems.Length)));
        }

        var visibleCount = 0;
        foreach (var diagnostic in visible)
        {
            visibleCount++;
            var resource = diagnostic.Resource is null ? string.Empty : $" [{diagnostic.Resource}]";
            var line = $"{text.Severity(diagnostic.Severity.ToString())} {diagnostic.Code}{resource}: "
                       + text.DiagnosticMessage(diagnostic.Code, diagnostic.Message);
            var writer = diagnostic.Severity == EpubDiagnosticSeverity.Information ? output : error;
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        if (visibleCount < items.Length)
        {
            await output.WriteLineAsync(text.Format(
                    "DiagnosticsLimited",
                    visibleCount.ToString(CultureInfo.InvariantCulture),
                    items.Length.ToString(CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedRoot, Path.TrimEndingDirectorySeparator(normalizedPath), comparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static async Task WriteHashAsync(TextWriter output, DocumentHash hash, CliTextCatalog text)
    {
        await output.WriteLineAsync(text.Format("LabelHash", hash.Algorithm, hash.Hash)).ConfigureAwait(false);
        await output.WriteLineAsync(text.Format("LabelCanonicalization", hash.CanonicalizationVersion))
            .ConfigureAwait(false);
    }

    private static DeviceClass GetDeviceClass(double width) => width switch
    {
        < AdaptiveLayoutEngine.MediumViewportMinimumWidth => DeviceClass.Phone,
        < AdaptiveLayoutEngine.LargeViewportMinimumWidth => DeviceClass.Tablet,
        _ => DeviceClass.Desktop,
    };

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string CssNumber(double value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);

    private static int CountNodes<TNode>(FlowDocument document)
        where TNode : DocumentNode =>
        document.Index.Locations.Count(static location => location.Node is TNode);
}
