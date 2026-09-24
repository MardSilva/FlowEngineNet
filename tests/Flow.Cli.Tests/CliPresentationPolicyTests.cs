using Spectre.Console;

namespace Flow.Cli.Tests;

public sealed class CliPresentationPolicyTests
{
    [Fact]
    public void Resolve_UsesRichProfileForInteractiveAnsiTerminal()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { Width = 120 };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Rich, profile.Mode);
        Assert.True(profile.IsInteractive);
        Assert.True(profile.UseColor);
        Assert.Equal(120, profile.Width);
    }

    [Fact]
    public void Resolve_UsesPlainProfileForRedirectedOutput()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { IsOutputRedirected = true };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.False(profile.IsInteractive);
        Assert.False(profile.UseColor);
    }

    [Fact]
    public void Resolve_UsesPlainProfileForRedirectedInput()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { IsInputRedirected = true };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.False(profile.IsInteractive);
    }

    [Fact]
    public void Resolve_KeepsRichOutputWhenOnlyErrorStreamIsRedirected()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { IsErrorRedirected = true };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Rich, profile.Mode);
        Assert.True(profile.IsInteractive);
    }

    [Fact]
    public void Resolve_UsesPlainProfileForNonConsoleWriter()
    {
        using var consoleOutput = new StringWriter();
        using var capturedOutput = new StringWriter();
        var terminal = new FakeTerminal(consoleOutput);

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, capturedOutput, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.False(profile.IsInteractive);
    }

    [Fact]
    public void Resolve_UsesPlainProfileWhenAnsiIsUnavailable()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { SupportsAnsi = false };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.True(profile.IsInteractive);
        Assert.False(profile.UseColor);
    }

    [Fact]
    public void Resolve_DisablesColorForExplicitOptionWithoutChangingRichMode()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output);

        var profile = CliPresentationPolicy.Resolve(
            Invocation(noColor: true), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Rich, profile.Mode);
        Assert.False(profile.UseColor);
    }

    [Fact]
    public void Resolve_ExplicitPlainTakesPrecedenceOverRichCapabilitiesAndNoColor()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output);

        var profile = CliPresentationPolicy.Resolve(
            Invocation(noColor: true, forcePlain: true),
            terminal,
            output,
            new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.True(profile.IsInteractive);
        Assert.False(profile.UseColor);
        Assert.False(profile.UseUnicode);
        Assert.False(profile.CanControlCursor);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Resolve_UsesPlainWhenCursorOrUnicodeSupportIsUnavailable(
        bool supportsCursor,
        bool supportsUnicode)
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output)
        {
            SupportsCursorControl = supportsCursor,
            SupportsUnicode = supportsUnicode,
        };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.False(profile.UseUnicode);
        Assert.False(profile.CanControlCursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("true")]
    public void Resolve_DisablesColorWhenNoColorEnvironmentExists(string value)
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output);

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment { NoColor = value });

        Assert.Equal(CliPresentationMode.Rich, profile.Mode);
        Assert.False(profile.UseColor);
    }

    [Fact]
    public void Resolve_UsesPlainFallbackWhenWidthIsUnavailable()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { WidthException = new IOException("unavailable") };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Plain, profile.Mode);
        Assert.Equal(CliPresentationProfile.DefaultWidth, profile.Width);
    }

    [Fact]
    public void Resolve_ClampsNarrowWidthWithoutLosingInteractiveMode()
    {
        using var output = new StringWriter();
        var terminal = new FakeTerminal(output) { Width = 5 };

        var profile = CliPresentationPolicy.Resolve(
            Invocation(), terminal, output, new FakeEnvironment());

        Assert.Equal(CliPresentationMode.Rich, profile.Mode);
        Assert.Equal(20, profile.Width);
    }

    [Fact]
    public void EscapeExternal_PreventsMarkupInterpretation()
    {
        Assert.Equal("[[book]][[red]]name[[/]]", CliMarkup.EscapeExternal("[book][red]name[/]"));
    }

    [Fact]
    public void WriteEscapedLine_WritesExternalValueLiterally()
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(output),
        });

        CliMarkup.WriteEscapedLine(console, "[book][red]name[/]");

        Assert.Equal($"[book][red]name[/]{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task SystemTerminalKeyboardRead_ObservesPreCancelledTokenWithoutReadingConsole()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new SystemFlowTerminal().ReadKeyAsync(cancellation.Token));
    }

    private static CliInvocationOptions Invocation(bool noColor = false, bool forcePlain = false) =>
        new(CliTextCatalog.DefaultCultureName, ShowBanner: false, UseColor: false, CommandArguments: [])
        {
            NoColor = noColor,
            ForcePlain = forcePlain,
        };

    private sealed class FakeEnvironment : IEnvironmentVariables
    {
        public string? NoColor { get; init; }

        public string? Get(string name) => name == "NO_COLOR" ? NoColor : null;
    }

    private sealed class FakeTerminal(TextWriter consoleOutput) : IFlowTerminal
    {
        public bool IsInputRedirected { get; init; }

        public bool IsOutputRedirected { get; init; }

        public bool IsErrorRedirected { get; init; }

        public bool SupportsAnsi { get; init; } = true;

        public bool SupportsCursorControl { get; init; } = true;

        public bool SupportsUnicode { get; init; } = true;

        public int Width { get; init; } = 80;

        public Exception? WidthException { get; init; }

        public bool IsConsoleOutput(TextWriter output) => ReferenceEquals(output, consoleOutput);

        public int GetWidth() => WidthException is null ? Width : throw WidthException;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }
}
