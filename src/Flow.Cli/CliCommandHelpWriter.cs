namespace Flow.Cli;

internal static class CliCommandHelpWriter
{
    public static async Task WriteAsync(
        CliCommandDescriptor command,
        TextWriter output,
        CliTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(text);

        await output.WriteLineAsync(text.Format(
            "CommandHelpHeading",
            command.Name,
            text.Get(command.TitleResourceKey))).ConfigureAwait(false);
        await output.WriteLineAsync(text.Get(command.DescriptionResourceKey)).ConfigureAwait(false);
        await WriteSectionAsync(output, text.Get("CommandHelpUsage"), [command.Usage]).ConfigureAwait(false);

        if (!command.Arguments.IsEmpty)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync(text.Get("CommandHelpArguments")).ConfigureAwait(false);
            foreach (var argument in command.Arguments)
            {
                await output.WriteLineAsync(text.Format(
                    "CommandHelpEntry",
                    argument.Syntax,
                    text.Get(RequirementKey(argument.Requirement)),
                    text.Get(argument.DescriptionResourceKey))).ConfigureAwait(false);
            }
        }

        if (!command.Options.IsEmpty)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync(text.Get("CommandHelpOptions")).ConfigureAwait(false);
            foreach (var option in command.Options)
            {
                await output.WriteLineAsync(text.Format(
                    "CommandHelpEntry",
                    option.Syntax,
                    text.Get(RequirementKey(option.Requirement)),
                    text.Get(option.DescriptionResourceKey))).ConfigureAwait(false);
            }
        }

        await WriteSectionAsync(output, text.Get("CommandHelpExamples"), command.Examples).ConfigureAwait(false);
        await WriteSectionAsync(
            output,
            text.Get("CommandHelpFileEffects"),
            [text.Get(command.FileEffectsResourceKey)]).ConfigureAwait(false);

        if (!command.SecurityResourceKeys.IsEmpty)
        {
            await WriteSectionAsync(
                output,
                text.Get("CommandHelpSecurity"),
                command.SecurityResourceKeys.Select(text.Get)).ConfigureAwait(false);
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(text.Get("CommandHelpExitCodes")).ConfigureAwait(false);
        foreach (var exitCode in command.ExitCodes)
        {
            await output.WriteLineAsync(text.Format(
                "CommandHelpExitCodeEntry",
                exitCode.Code,
                text.Get(exitCode.DescriptionResourceKey))).ConfigureAwait(false);
        }
    }

    private static async Task WriteSectionAsync(
        TextWriter output,
        string title,
        IEnumerable<string> entries)
    {
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(title).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            await output.WriteLineAsync($"  {entry}").ConfigureAwait(false);
        }
    }

    private static string RequirementKey(CliRequirement requirement) => requirement switch
    {
        CliRequirement.Required => "CommandHelpRequired",
        CliRequirement.Optional => "CommandHelpOptional",
        CliRequirement.Conditional => "CommandHelpConditional",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, null),
    };
}
