namespace Flow.Cli.Tests;

public sealed class CliMenuTests
{
    [Fact]
    public void ParserAndCatalogDescribeMenuWithoutOfferingItInsideItself()
    {
        var parser = new CliCommandParser();

        Assert.IsType<MenuCommand>(parser.Parse(["menu"]).Command);
        Assert.False(parser.Parse(["menu", "unexpected"]).IsSuccess);
        Assert.True(CliCommandCatalog.TryGet("menu", out var descriptor));
        Assert.False(descriptor.AvailableInInteractiveMenu);
        Assert.Equal("flow menu", descriptor.Usage);
    }

    [Fact]
    public void InteractiveAssistantsAreLimitedToDeliveredWorkflows()
    {
        Assert.Equal(
            [
                "corpus",
                "epub-inspect",
                "epub-inventory",
                "epub-inventory-matrix",
                "epub-inventory-qualify",
                "epub-inventory-review",
                "epub-qualify",
                "epub-review",
                "execution-clean",
                "execution-status",
                "hash",
                "import",
                "inspect",
                "render",
                "validate",
            ],
            CliCommandCatalog.All
                .Where(command => CliMenuController.SupportsAssistant(command.Name))
                .Select(static command => command.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.False(CliMenuController.SupportsAssistant("sample"));
    }

    [Theory]
    [InlineData("en-US", "EPUB books", "book.epub", "File effects", "guide EPUB inspection")]
    [InlineData("pt-BR", "Livros EPUB", "book.epub", "Efeitos nos arquivos", "orientar a inspeção")]
    public void ModelAndDetailsReuseLocalizedCatalog(
        string culture,
        string epubGroupTitle,
        string requiredInput,
        string effectsHeading,
        string readOnlyNotice)
    {
        var text = new CliTextCatalog(culture);
        var model = CliMenuModel.Create(text);
        var epubGroup = model.Groups.Single(group => group.Group == CliCommandGroup.EpubBooks);
        var import = epubGroup.Commands.Single(command => command.Name == "import");
        var details = CliMenuDetails.Create(import, text);
        var welcome = CliMenuWelcome.Create(text);

        Assert.Equal(epubGroupTitle, epubGroup.Title);
        Assert.Contains(details.RequiredInputs, input => input.Contains(requiredInput, StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(details.FileEffects));
        Assert.NotEmpty(details.SecurityNotes);
        Assert.Equal(effectsHeading, text.Get("MenuDetailsOutputs"));
        Assert.Contains(".flow.json", welcome.Description, StringComparison.Ordinal);
        Assert.Contains(readOnlyNotice, welcome.ReadOnlyNotice, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Groups.SelectMany(group => group.Commands), command => command.Name == "menu");
        Assert.DoesNotContain(model.Groups.SelectMany(group => group.Commands), command => command.Name == "help");
        var advanced = model.Groups.Single(group => group.Group is null);
        Assert.Equal(text.Get("MenuAdvancedTitle"), advanced.Title);
        Assert.Equal(9, advanced.Commands.Length);
        Assert.All(
            advanced.Commands,
            command => Assert.True(
                command.Group is CliCommandGroup.CorpusQuality or CliCommandGroup.Maintenance));
    }

    [Fact]
    public async Task ControllerShowsDetailsReturnsBackAndExitsWithoutExecutingCommand()
    {
        var view = new ScriptedMenuView();
        using var output = new StringWriter();

        var exitCode = await new CliMenuController(view).RunAsync(
            output,
            new CliTextCatalog("en-US"),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, view.MainSelectionCount);
        Assert.Equal(2, view.CommandSelectionCount);
        var details = Assert.Single(view.ShownDetails);
        Assert.Equal("import", details.CommandName);
        var welcome = Assert.Single(view.ShownWelcome);
        Assert.Contains("semantic documents", welcome.Description, StringComparison.Ordinal);
        Assert.Contains("guide EPUB inspection", welcome.ReadOnlyNotice, StringComparison.Ordinal);
        Assert.Contains("Interactive menu closed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControllerTreatsEscapeAsCleanCancellation()
    {
        var view = new CancellingMenuView();
        using var output = new StringWriter();

        var exitCode = await new CliMenuController(view).RunAsync(
            output,
            new CliTextCatalog("pt-BR"),
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Menu interativo cancelado", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nenhuma operação foi executada", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "FLOWCLI_MENU_REQUIRES_INTERACTIVE", "flow help")]
    [InlineData("pt-BR", "FLOWCLI_MENU_REQUIRES_INTERACTIVE", "flow help")]
    public async Task ApplicationRefusesMenuWhenOutputIsCaptured(
        string culture,
        string code,
        string equivalentCommand)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FlowCliApplication.CreateDefault().RunAsync(
            ["--language", culture, "menu"],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.StartsWith($"{code}:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(equivalentCommand, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplicationRunsInjectedMenuInRichNoColorMode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var menu = new RecordingMenu();
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            menu);

        var exitCode = await application.RunAsync(
            ["--language", "pt-BR", "--no-color", "menu"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.True(menu.WasRun);
        Assert.NotNull(menu.Presentation);
        Assert.Equal(CliPresentationMode.Rich, menu.Presentation.Mode);
        Assert.False(menu.Presentation.UseColor);
        Assert.Equal("pt-BR", menu.CultureName);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RichAssistantScreenClearsVisibleContentAndScrollbackBeforeRendering()
    {
        using var output = new StringWriter();
        var view = new SpectreCliMenuView(
            output,
            new CliTextCatalog("en-US"),
            new CliPresentationProfile(
                CliPresentationMode.Rich,
                IsInteractive: true,
                UseColor: false,
                Width: 80));

        await view.ShowSummaryAsync(
            new CliAssistantSummary(
                "Summary",
                [("Source", "book.epub")],
                []),
            CancellationToken.None);

        var rendered = output.ToString();
        Assert.Contains("\u001b[2J", rendered, StringComparison.Ordinal);
        Assert.Contains("\u001b[3J", rendered, StringComparison.Ordinal);
        Assert.Contains("Summary", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplicationRefusesMenuWhenOnlyInputIsRedirected()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var menu = new RecordingMenu();
        var application = FlowCliApplication.CreateDefault(
            new InputRedirectedTerminal(output),
            new EmptyEnvironment(),
            menu);

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(1, exitCode);
        Assert.False(menu.WasRun);
        Assert.StartsWith(
            $"{CliMenuDiagnosticCodes.RequiresInteractiveTerminal}:",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlCUsesExistingCancellationExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            new CancellingMenu());

        var exitCode = await application.RunAsync(["menu"], output, error, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("FLOWCLI_CANCELLED", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class ScriptedMenuView : ICliMenuView
    {
        public int MainSelectionCount { get; private set; }

        public int CommandSelectionCount { get; private set; }

        public List<CliMenuDetails> ShownDetails { get; } = [];

        public List<CliMenuWelcome> ShownWelcome { get; } = [];

        public Task ShowWelcomeAsync(
            CliMenuWelcome welcome,
            CancellationToken cancellationToken)
        {
            ShownWelcome.Add(welcome);
            return Task.CompletedTask;
        }

        public Task<CliMenuMainSelection> SelectGroupAsync(
            CliMenuModel model,
            CancellationToken cancellationToken)
        {
            MainSelectionCount++;
            return Task.FromResult(MainSelectionCount == 1
                ? new CliMenuMainSelection(
                    CliMenuMainAction.OpenGroup,
                    model.Groups.Single(group => group.Group == CliCommandGroup.EpubBooks))
                : new CliMenuMainSelection(CliMenuMainAction.Exit));
        }

        public Task<CliMenuCommandSelection> SelectCommandAsync(
            CliMenuGroup group,
            CancellationToken cancellationToken)
        {
            CommandSelectionCount++;
            return Task.FromResult(CommandSelectionCount == 1
                ? new CliMenuCommandSelection(
                    CliMenuCommandAction.ShowDetails,
                    group.Commands.Single(command => command.Name == "import"))
                : new CliMenuCommandSelection(CliMenuCommandAction.Back));
        }

        public Task<CliMenuDetailsAction> ShowDetailsAsync(
            CliMenuDetails details,
            bool assistantAvailable,
            CancellationToken cancellationToken)
        {
            ShownDetails.Add(details);
            Assert.True(assistantAvailable);
            return Task.FromResult(CliMenuDetailsAction.Back);
        }
    }

    private sealed class CancellingMenuView : ICliMenuView
    {
        public Task ShowWelcomeAsync(
            CliMenuWelcome welcome,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CliMenuMainSelection> SelectGroupAsync(
            CliMenuModel model,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CliMenuMainSelection(CliMenuMainAction.Cancel));

        public Task<CliMenuCommandSelection> SelectCommandAsync(
            CliMenuGroup group,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<CliMenuDetailsAction> ShowDetailsAsync(
            CliMenuDetails details,
            bool assistantAvailable,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class RecordingMenu : ICliMenu
    {
        public bool WasRun { get; private set; }

        public CliPresentationProfile? Presentation { get; private set; }

        public string? CultureName { get; private set; }

        public Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken)
        {
            WasRun = true;
            Presentation = presentation;
            CultureName = text.Culture.Name;
            return Task.FromResult(0);
        }
    }

    private sealed class CancellingMenu : ICliMenu
    {
        public Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken) => Task.FromCanceled<int>(cancellationToken);
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

        public int GetWidth() => 80;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }

    private sealed class InputRedirectedTerminal(TextWriter output) : IFlowTerminal
    {
        public bool IsInputRedirected => true;

        public bool IsOutputRedirected => false;

        public bool IsErrorRedirected => false;

        public bool SupportsAnsi => true;

        public bool IsConsoleOutput(TextWriter candidate) => ReferenceEquals(output, candidate);

        public int GetWidth() => 80;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }
}
