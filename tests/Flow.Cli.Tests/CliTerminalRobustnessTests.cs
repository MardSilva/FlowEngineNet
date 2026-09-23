namespace Flow.Cli.Tests;

public sealed class CliTerminalRobustnessTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public async Task ExplicitPlainHelpUsesNoAnsiOrBoxDrawing(string culture)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new RecordingTerminal(output);
        var application = FlowCliApplication.CreateDefault(terminal, new EmptyEnvironment());

        var exitCode = await application.RunAsync(
            ["--language", culture, "--plain", "help"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("--plain", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', output.ToString());
        Assert.DoesNotContain('┌', output.ToString());
        Assert.DoesNotContain('─', output.ToString());
        Assert.DoesNotContain('│', output.ToString());
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task ExplicitPlainMenuRefusesToInferSelectionsAndPointsToDirectCommands()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new RecordingTerminal(output);
        var application = FlowCliApplication.CreateDefault(terminal, new EmptyEnvironment());

        var exitCode = await application.RunAsync(["--plain", "menu"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains(CliMenuDiagnosticCodes.RequiresInteractiveTerminal, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("flow help", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task UnexpectedEndOfInteractiveInputReturnsStableDiagnosticAndRestoresTerminal()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new RecordingTerminal(output);
        var application = FlowCliApplication.CreateDefault(
            terminal,
            new EmptyEnvironment(),
            new EndOfInputMenu());

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(1, exitCode);
        Assert.StartsWith(
            $"{CliTerminalErrorCodes.InteractiveInputUnavailable}:",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task InteractiveDrawingFailureReturnsStableDiagnosticAndRestoresTerminal()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new RecordingTerminal(output);
        var application = FlowCliApplication.CreateDefault(
            terminal,
            new EmptyEnvironment(),
            new DrawingFailureMenu());

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(1, exitCode);
        Assert.StartsWith(
            $"{CliMenuDiagnosticCodes.PresentationFailed}:",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task CancellationPreservesExitCodeAndRestoresTerminal()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new RecordingTerminal(output);
        var application = FlowCliApplication.CreateDefault(
            terminal,
            new EmptyEnvironment(),
            new CancelledMenu());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await application.RunAsync(["menu"], output, error, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("FLOWCLI_CANCELLED", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public void ConsoleOutputReadsCurrentWidthAndFallsBackCanBeAppliedByCaller()
    {
        using var output = new StringWriter();
        var width = 40;
        var consoleOutput = new CliAnsiConsoleOutput(output, () => width);

        Assert.Equal(40, consoleOutput.Width);
        width = 112;
        Assert.Equal(112, consoleOutput.Width);
    }

    [Fact]
    public void MarkupEscapePreservesAccentsBidiAndLiteralBrackets()
    {
        const string value = "ação العربية [red]texto[/]";

        var escaped = CliMarkup.EscapeExternal(value);

        Assert.Contains("ação العربية", escaped, StringComparison.Ordinal);
        Assert.Contains("[[red]]texto[[/]]", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInteractiveWorkflowRetainsAVisibleDirectCommand()
    {
        Assert.All(
            CliCommandCatalog.All.Where(static command => command.AvailableInInteractiveMenu),
            command =>
            {
                Assert.StartsWith($"flow {command.Name}", command.Usage, StringComparison.Ordinal);
                Assert.True(CliCommandCatalog.TryGet(command.Name, out _));
            });
    }

    private sealed class RecordingTerminal(TextWriter output) : IFlowTerminal
    {
        public int RestoreCount { get; private set; }

        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool IsErrorRedirected => false;

        public bool SupportsAnsi => true;

        public bool SupportsCursorControl => true;

        public bool SupportsUnicode => true;

        public bool IsConsoleOutput(TextWriter candidate) => ReferenceEquals(output, candidate);

        public int GetWidth() => 80;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new EndOfStreamException());

        public void RestoreTerminalState() => RestoreCount++;
    }

    private sealed class EmptyEnvironment : IEnvironmentVariables
    {
        public string? Get(string name) => null;
    }

    private sealed class EndOfInputMenu : ICliMenu
    {
        public Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken) => throw new EndOfStreamException();
    }

    private sealed class CancelledMenu : ICliMenu
    {
        public Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken) => Task.FromCanceled<int>(cancellationToken);
    }

    private sealed class DrawingFailureMenu : ICliMenu
    {
        public Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken) => throw new InvalidOperationException("drawing failed");
    }
}
