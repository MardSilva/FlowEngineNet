using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Cli;

public sealed class FlowCliApplication
{
    private readonly CliCommandParser _parser;
    private readonly CliOperations _operations;

    public FlowCliApplication(CliCommandParser parser, CliOperations operations)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(operations);
        _parser = parser;
        _operations = operations;
    }

    public static FlowCliApplication CreateDefault() =>
        new(
            new CliCommandParser(),
            new CliOperations(
                new FlowJsonDocumentSerializer(),
                new DocumentValidator(),
                new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
                new AdaptiveLayoutEngine(),
                new HtmlDocumentRenderer()));

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = _parser.Parse(arguments);
        if (!parseResult.IsSuccess)
        {
            await error.WriteLineAsync(parseResult.Error).ConfigureAwait(false);
            return 1;
        }

        try
        {
            return await _operations.ExecuteAsync(parseResult.Command!, output, cancellationToken).ConfigureAwait(false);
        }
        catch (FlowSerializationException exception)
        {
            await error.WriteLineAsync($"{exception.Code}: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            await error.WriteLineAsync($"Error: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
