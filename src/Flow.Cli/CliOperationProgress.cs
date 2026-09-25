namespace Flow.Cli;

internal enum CliOperationPhase
{
    Preparing,
    Inspecting,
    Importing,
    Validating,
    Hashing,
    Rendering,
    ProcessingCorpus,
    MaintainingExecution,
    Writing,
}

internal enum CliOperationProgressState
{
    Started,
    Advanced,
    Completed,
    Failed,
}

internal sealed record CliOperationProgressUpdate
{
    public CliOperationProgressUpdate(
        CliOperationPhase phase,
        CliOperationProgressState state,
        string messageResourceKey,
        long? completedUnits = null,
        long? totalUnits = null)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(messageResourceKey);
        if (completedUnits.HasValue != totalUnits.HasValue)
        {
            throw new ArgumentException("Completed and total units must either both be present or both be absent.");
        }

        if (completedUnits < 0 || totalUnits <= 0 || completedUnits > totalUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(completedUnits));
        }

        Phase = phase;
        State = state;
        MessageResourceKey = messageResourceKey;
        CompletedUnits = completedUnits;
        TotalUnits = totalUnits;
    }

    public CliOperationPhase Phase { get; }

    public CliOperationProgressState State { get; }

    public string MessageResourceKey { get; }

    public long? CompletedUnits { get; }

    public long? TotalUnits { get; }
}

internal interface ICliOperationProgress
{
    public void Report(CliOperationProgressUpdate update);
}

internal sealed class CliPlainOperationProgress(TextWriter output, CliTextCatalog text) : ICliOperationProgress
{
    private readonly object _sync = new();
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly CliTextCatalog _text = text ?? throw new ArgumentNullException(nameof(text));

    public void Report(CliOperationProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.State is not (CliOperationProgressState.Started
            or CliOperationProgressState.Completed
            or CliOperationProgressState.Failed))
        {
            return;
        }

        var stateKey = update.State switch
        {
            CliOperationProgressState.Started => "ProgressStarted",
            CliOperationProgressState.Completed => "ProgressCompleted",
            CliOperationProgressState.Failed => "ProgressFailed",
            _ => throw new ArgumentOutOfRangeException(nameof(update)),
        };
        var line = _text.Format(stateKey, _text.Get(update.MessageResourceKey));
        lock (_sync)
        {
            _output.WriteLine(line);
        }
    }
}

internal static class CliOperationProgressPlan
{
    public static (CliOperationPhase Phase, string MessageResourceKey) For(CliCommand command) => command switch
    {
        InspectEpubCommand or InspectCommand => (CliOperationPhase.Inspecting, "ProgressPhaseInspecting"),
        ImportEpubCommand => (CliOperationPhase.Importing, "ProgressPhaseImporting"),
        ValidateCommand => (CliOperationPhase.Validating, "ProgressPhaseValidating"),
        HashCommand => (CliOperationPhase.Hashing, "ProgressPhaseHashing"),
        RenderHtmlCommand or RenderHtmlBookCommand => (CliOperationPhase.Rendering, "ProgressPhaseRendering"),
        InventoryEpubCommand or QualifyEpubInventoryCommand or ClassifyEpubInventoryCommand
            or ReviewEpubInventoryCommand or CorpusCommand or QualifyEpubCommand or ReviewEpubCommand =>
            (CliOperationPhase.ProcessingCorpus, "ProgressPhaseProcessingCorpus"),
        ExecutionStatusCommand or ExecutionCleanCommand =>
            (CliOperationPhase.MaintainingExecution, "ProgressPhaseMaintainingExecution"),
        UpdateCheckCommand => (CliOperationPhase.Preparing, "ProgressPhaseCheckingUpdates"),
        SampleCommand => (CliOperationPhase.Writing, "ProgressPhaseWriting"),
        _ => (CliOperationPhase.Preparing, "ProgressPhasePreparing"),
    };
}
