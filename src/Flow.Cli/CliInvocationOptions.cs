namespace Flow.Cli;

/// <summary>Contains presentation-only options parsed before a CLI command.</summary>
public sealed record CliInvocationOptions(
    string CultureName,
    bool ShowBanner,
    bool UseColor,
    IReadOnlyList<string> CommandArguments);

/// <summary>Parses global output options without changing command syntax.</summary>
public static class CliInvocationOptionsParser
{
    public static CliInvocationOptionsParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var cultureName = CliTextCatalog.DefaultCultureName;
        var languageSpecified = false;
        var showBanner = false;
        var noColor = false;
        var index = 0;
        while (index < arguments.Count && arguments[index].StartsWith("-", StringComparison.Ordinal))
        {
            var option = arguments[index];
            if (option is "--help" or "-h")
            {
                break;
            }

            switch (option)
            {
                case "--language":
                    if (languageSpecified)
                    {
                        return CliInvocationOptionsParseResult.Failure(
                            "FLOWCLI_DUPLICATE_OPTION",
                            "ErrorDuplicateOption",
                            option);
                    }

                    if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    {
                        return CliInvocationOptionsParseResult.Failure(
                            "FLOWCLI_USAGE",
                            "ErrorOptionRequiresValue",
                            option,
                            "flow [--language <en-US|pt-BR>] [--banner] [--no-color] <command>");
                    }

                    if (!CliTextCatalog.TryNormalizeCulture(arguments[index], out cultureName))
                    {
                        return CliInvocationOptionsParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE",
                            "ErrorLanguageValue",
                            arguments[index]);
                    }

                    languageSpecified = true;
                    index++;
                    break;
                case "--banner" when !showBanner:
                    showBanner = true;
                    index++;
                    break;
                case "--no-color" when !noColor:
                    noColor = true;
                    index++;
                    break;
                case "--banner" or "--no-color":
                    return CliInvocationOptionsParseResult.Failure(
                        "FLOWCLI_DUPLICATE_OPTION",
                        "ErrorDuplicateOption",
                        option);
                default:
                    return CliInvocationOptionsParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownGlobalOption",
                        option);
            }
        }

        var commandArguments = arguments.Skip(index).ToArray();
        return CliInvocationOptionsParseResult.Success(
            new CliInvocationOptions(cultureName, showBanner, UseColor: false, commandArguments));
    }
}

public sealed record CliInvocationOptionsParseResult
{
    private CliInvocationOptionsParseResult(
        CliInvocationOptions? options,
        string? diagnosticCode,
        string? resourceKey,
        object?[] arguments)
    {
        Options = options;
        DiagnosticCode = diagnosticCode;
        ResourceKey = resourceKey;
        Arguments = arguments;
    }

    public CliInvocationOptions? Options { get; }

    public string? DiagnosticCode { get; }

    public string? ResourceKey { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public bool IsSuccess => Options is not null;

    public static CliInvocationOptionsParseResult Success(CliInvocationOptions options) =>
        new(options, null, null, []);

    public static CliInvocationOptionsParseResult Failure(string code, string resourceKey, params object?[] arguments) =>
        new(null, code, resourceKey, arguments);
}
