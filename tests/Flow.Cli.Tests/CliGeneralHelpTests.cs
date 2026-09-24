namespace Flow.Cli.Tests;

public sealed class CliGeneralHelpTests
{
    [Fact]
    public void Model_UsesCatalogOrderAndIncludesEachCommandOnce()
    {
        var model = CliGeneralHelpModel.Create(new CliTextCatalog("en-US"));

        Assert.Equal(Enum.GetValues<CliCommandGroup>(), model.Groups.Select(group => group.Group));
        Assert.Equal(
            CliCommandCatalog.All.Select(command => command.Name).Order(StringComparer.Ordinal),
            model.Groups.SelectMany(group => group.Commands).Select(command => command.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            CliCommandCatalog.All.Length,
            model.Groups.Sum(group => group.Commands.Length));
    }

    [Theory]
    [InlineData("en-US", "Getting started", "EPUB books", "Flow documents", "Corpus and quality", "Maintenance")]
    [InlineData("pt-BR", "Início", "Livros EPUB", "Documentos Flow", "Corpus e qualidade", "Manutenção")]
    public async Task Plain_IsLocalizedDeterministicAndLimitedToEightyColumns(
        string culture,
        params string[] groupTitles)
    {
        var first = await RenderAsync(culture, CliPresentationProfile.Plain());
        var second = await RenderAsync(culture, CliPresentationProfile.Plain());

        Assert.Equal(first, second);
        Assert.DoesNotContain("\u001b", first, StringComparison.Ordinal);
        Assert.DoesNotContain('│', first);
        Assert.All(Lines(first), line => Assert.InRange(line.Length, 0, 80));
        foreach (var title in groupTitles)
        {
            Assert.Contains(title, first, StringComparison.Ordinal);
        }

        foreach (var command in CliCommandCatalog.All)
        {
            Assert.Contains($"  {command.Name}{Environment.NewLine}", first, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--qualification-sha256", first, StringComparison.Ordinal);
        Assert.DoesNotContain("<book.epub>", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rich_WideUsesCompactRowsAndNarrowStacksDescriptions()
    {
        var wide = await RenderAsync("en-US", Rich(width: 100));
        var narrow = await RenderAsync("en-US", Rich(width: 50));

        var wideCommandLine = Lines(wide).Single(line =>
            line.TrimStart().StartsWith("epub-inventory-qualify ", StringComparison.Ordinal));
        var narrowCommandLine = Lines(narrow).Single(line => line.Trim() == "epub-inventory-qualify");

        Assert.True(wideCommandLine.Trim().Length > "epub-inventory-qualify".Length);
        Assert.Equal("epub-inventory-qualify", narrowCommandLine.Trim());
        Assert.All(Lines(wide), line => Assert.InRange(line.Length, 0, 100));
        Assert.All(Lines(narrow), line => Assert.InRange(line.Length, 0, 50));
        Assert.DoesNotContain("--qualification-sha256", wide, StringComparison.Ordinal);
        Assert.DoesNotContain("--qualification-sha256", narrow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rich_NoColorPreservesHierarchyWithoutAnsiSequences()
    {
        var noColor = await RenderAsync("pt-BR", Rich(width: 80));
        var color = await RenderAsync("pt-BR", Rich(width: 80, useColor: true));

        Assert.DoesNotContain("\u001b", noColor, StringComparison.Ordinal);
        Assert.Contains("\u001b", color, StringComparison.Ordinal);
        Assert.Contains("Início", noColor, StringComparison.Ordinal);
        Assert.Contains("Livros EPUB", noColor, StringComparison.Ordinal);
        Assert.Contains("import", noColor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_UsesPlainHelpWhenOutputIsCaptured()
    {
        using var consoleOutput = new StringWriter();
        using var capturedOutput = new StringWriter();
        using var error = new StringWriter();
        var terminal = new FakeTerminal(consoleOutput, width: 100);
        var application = FlowCliApplication.CreateDefault(terminal, new FakeEnvironment());

        var exitCode = await application.RunAsync(["help"], capturedOutput, error);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("\u001b", capturedOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains($"  import{Environment.NewLine}", capturedOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_UsesRichHelpForInteractiveTerminal()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var terminal = new FakeTerminal(output, width: 100);
        var application = FlowCliApplication.CreateDefault(terminal, new FakeEnvironment());

        var exitCode = await application.RunAsync(["help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\u001b", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Getting started", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("epub-inventory-qualify", output.ToString(), StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(string culture, CliPresentationProfile profile)
    {
        using var output = new StringWriter();
        await CliGeneralHelpWriter.WriteAsync(output, new CliTextCatalog(culture), profile);
        return output.ToString();
    }

    private static CliPresentationProfile Rich(int width, bool useColor = false) =>
        new(CliPresentationMode.Rich, IsInteractive: true, useColor, width);

    private static string[] Lines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private sealed class FakeEnvironment : IEnvironmentVariables
    {
        public string? Get(string name) => null;
    }

    private sealed class FakeTerminal(TextWriter output, int width) : IFlowTerminal
    {
        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool IsErrorRedirected => false;

        public bool SupportsAnsi => true;

        public bool IsConsoleOutput(TextWriter candidate) => ReferenceEquals(output, candidate);

        public int GetWidth() => width;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }
}
