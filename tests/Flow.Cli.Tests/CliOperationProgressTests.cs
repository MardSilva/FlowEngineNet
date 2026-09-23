using System.Collections.Immutable;

namespace Flow.Cli.Tests;

public sealed class CliOperationProgressTests
{
    [Fact]
    public void UpdateRejectsPartialOrInventedUnitRanges()
    {
        Assert.Throws<ArgumentException>(() => new CliOperationProgressUpdate(
            CliOperationPhase.Importing,
            CliOperationProgressState.Started,
            "ProgressPhaseImporting",
            completedUnits: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CliOperationProgressUpdate(
            CliOperationPhase.Importing,
            CliOperationProgressState.Advanced,
            "ProgressPhaseImporting",
            completedUnits: 3,
            totalUnits: 2));
    }

    [Theory]
    [InlineData("en-US", "[START] Inspecting the document", "[DONE] Inspecting the document")]
    [InlineData("pt-BR", "[INÍCIO] Inspecionando o documento", "[CONCLUÍDO] Inspecionando o documento")]
    public void PlainProgressWritesStableLinesWithoutTerminalControlCharacters(
        string culture,
        string expectedStart,
        string expectedCompletion)
    {
        using var output = new StringWriter();
        var progress = new CliPlainOperationProgress(output, new CliTextCatalog(culture));

        progress.Report(new CliOperationProgressUpdate(
            CliOperationPhase.Inspecting,
            CliOperationProgressState.Started,
            "ProgressPhaseInspecting"));
        progress.Report(new CliOperationProgressUpdate(
            CliOperationPhase.Inspecting,
            CliOperationProgressState.Completed,
            "ProgressPhaseInspecting"));

        var rendered = output.ToString();
        Assert.Contains(expectedStart, rendered, StringComparison.Ordinal);
        Assert.Contains(expectedCompletion, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', rendered);
        Assert.DoesNotContain('\b', rendered);
        Assert.DoesNotContain('\r', rendered.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InteractiveExecutionReportsOneRealUnitAndCapturesItsOutput()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var destination = Path.Combine(Path.GetTempPath(), $"flow-status-{Guid.NewGuid():N}.json");
        var menu = new ProgressRecordingMenu(new ExecutionStatusCommand(destination, null));
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            menu);

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Empty(error.ToString());
        var result = Assert.IsType<CliMenuExecutionResult>(menu.Result);
        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.OutputLines);
        Assert.Empty(result.DiagnosticLines);
        Assert.True(result.Duration >= TimeSpan.Zero);
        Assert.Collection(
            menu.Progress.Updates,
            started =>
            {
                Assert.Equal(CliOperationPhase.MaintainingExecution, started.Phase);
                Assert.Equal(CliOperationProgressState.Started, started.State);
                Assert.Equal(0, started.CompletedUnits);
                Assert.Equal(1, started.TotalUnits);
            },
            completed =>
            {
                Assert.Equal(CliOperationPhase.MaintainingExecution, completed.Phase);
                Assert.Equal(CliOperationProgressState.Completed, completed.State);
                Assert.Equal(1, completed.CompletedUnits);
                Assert.Equal(1, completed.TotalUnits);
            });
    }

    [Fact]
    public async Task InteractiveFailureKeepsOriginalDiagnosticCodeInResult()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.flow.json");
        var menu = new ProgressRecordingMenu(new InspectCommand(missing));
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            menu);

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Empty(error.ToString());
        var result = Assert.IsType<CliMenuExecutionResult>(menu.Result);
        var diagnostic = Assert.Single(result.DiagnosticLines);
        Assert.StartsWith("FLOWCLI_OPERATION_FAILED:", diagnostic, StringComparison.Ordinal);
        Assert.Equal(CliOperationProgressState.Failed, menu.Progress.Updates[^1].State);
    }

    [Fact]
    public async Task RichResultUsesTextAndSymbolsInAdditionToColor()
    {
        using var output = new StringWriter();
        var view = new SpectreCliMenuView(
            output,
            new CliTextCatalog("en-US"),
            new CliPresentationProfile(CliPresentationMode.Rich, IsInteractive: true, UseColor: false, Width: 100));
        var result = new CliMenuExecutionResult(
            1,
            ["result.json"],
            TimeSpan.FromMilliseconds(12.5),
            ["Processed: 1"],
            ["FLOWCLI_TEST: preserved diagnostic"]);

        await view.ShowExecutionResultAsync(result, CancellationToken.None);

        var rendered = output.ToString();
        Assert.Contains("[ERROR] Completed with failure", rendered, StringComparison.Ordinal);
        Assert.Contains("12.5 ms", rendered, StringComparison.Ordinal);
        Assert.Contains("FLOWCLI_TEST: preserved diagnostic", rendered, StringComparison.Ordinal);
        Assert.Contains("result.json", rendered, StringComparison.Ordinal);
    }

    private sealed class ProgressRecordingMenu(CliCommand command) : ICliMenu
    {
        private readonly CliCommand _command = command;

        public RecordingProgress Progress { get; } = new();

        public CliMenuExecutionResult? Result { get; private set; }

        public async Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken)
        {
            Result = await executor(_command, Progress, cancellationToken);
            return Result.ExitCode;
        }
    }

    private sealed class RecordingProgress : ICliOperationProgress
    {
        public List<CliOperationProgressUpdate> Updates { get; } = [];

        public void Report(CliOperationProgressUpdate update) => Updates.Add(update);
    }

    private sealed class EmptyEnvironment : IEnvironmentVariables
    {
        public string? Get(string name) => null;
    }

    private sealed class InteractiveTerminal(TextWriter output) : IFlowTerminal
    {
        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool IsErrorRedirected => false;

        public bool SupportsAnsi => true;

        public bool IsConsoleOutput(TextWriter candidate) => ReferenceEquals(output, candidate);

        public int GetWidth() => 100;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }
}
