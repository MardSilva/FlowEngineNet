namespace Flow.Cli.Tests;

public sealed class CliCommandCatalogTests
{
    [Fact]
    public void CatalogMatchesEveryPublicParserCommand()
    {
        var catalogNames = CliCommandCatalog.All.Select(static command => command.Name).Order().ToArray();
        var parserNames = CliCommandParser.SupportedCommandNames.Order().ToArray();

        Assert.Equal(parserNames, catalogNames);
        Assert.Equal(catalogNames.Length, catalogNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CatalogExamplesAreAcceptedAndCoverEveryAdvertisedOption()
    {
        var parser = new CliCommandParser();

        foreach (var command in CliCommandCatalog.All)
        {
            var coveredOptions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var example in command.Examples)
            {
                var tokens = example.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal("flow", tokens[0]);

                var arguments = tokens.Skip(1).ToArray();
                var result = parser.Parse(arguments);
                Assert.True(result.IsSuccess, $"Catalog example did not parse: {example}. {result.Error}");

                foreach (var token in arguments.Where(static token => token.StartsWith("--", StringComparison.Ordinal)))
                {
                    coveredOptions.Add(token);
                    Assert.Contains(command.Options, option => option.Name == token);
                }
            }

            Assert.Equal(
                command.Options.Select(static option => option.Name).Order(),
                coveredOptions.Order());
        }
    }

    [Fact]
    public void CatalogEntriesAreCompleteAndUnambiguous()
    {
        foreach (var command in CliCommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name));
            Assert.False(string.IsNullOrWhiteSpace(command.TitleResourceKey));
            Assert.False(string.IsNullOrWhiteSpace(command.DescriptionResourceKey));
            Assert.StartsWith($"flow {command.Name}", command.Usage, StringComparison.Ordinal);
            Assert.NotEmpty(command.Examples);
            Assert.NotEmpty(command.ExitCodes);
            Assert.Equal(
                command.Options.Length,
                command.Options.Select(static option => option.Name).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Theory]
    [InlineData(CliTextCatalog.DefaultCultureName)]
    [InlineData(CliTextCatalog.PortugueseBrazilCultureName)]
    public async Task EveryCommandHasLocalizedDetailedHelp(string cultureName)
    {
        var text = new CliTextCatalog(cultureName);

        foreach (var command in CliCommandCatalog.All)
        {
            using var output = new StringWriter();
            await CliCommandHelpWriter.WriteAsync(command, output, text);

            var help = output.ToString();
            Assert.Contains(command.Name, help, StringComparison.Ordinal);
            Assert.Contains(command.Usage, help, StringComparison.Ordinal);
            Assert.Contains(text.Get(command.TitleResourceKey), help, StringComparison.Ordinal);
            Assert.Contains(text.Get(command.FileEffectsResourceKey), help, StringComparison.Ordinal);
        }
    }
}
