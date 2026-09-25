using System.Collections.Immutable;
using System.Text;

namespace Flow.Application;

/// <summary>Identifies the shell syntax used only to display or copy an equivalent command.</summary>
public enum FlowCommandShell
{
    PowerShell,
    CommandPrompt,
}

/// <summary>
/// Contains a shell-specific command representation for display and copying.
/// It is not an execution request and must never be used as a subprocess transport.
/// </summary>
public sealed record FlowCommandDisplay
{
    public FlowCommandDisplay(
        FlowCommandShell shell,
        string executable,
        IEnumerable<string> arguments,
        string text)
    {
        if (!Enum.IsDefined(shell))
        {
            throw new ArgumentOutOfRangeException(nameof(shell));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Shell = shell;
        Executable = executable;
        Arguments = arguments.ToImmutableArray();
        Text = text;
    }

    public FlowCommandShell Shell { get; }

    public string Executable { get; }

    public ImmutableArray<string> Arguments { get; }

    public string Text { get; }
}

/// <summary>Formats command text for a user-selected shell without executing it.</summary>
public static class FlowCommandDisplayFormatter
{
    public static FlowCommandDisplay Format(
        FlowCommandShell shell,
        string executable,
        IEnumerable<string> arguments)
    {
        if (!Enum.IsDefined(shell))
        {
            throw new ArgumentOutOfRangeException(nameof(shell));
        }

        ValidateToken(executable, nameof(executable));
        ArgumentNullException.ThrowIfNull(arguments);
        var materialized = arguments.ToImmutableArray();
        for (var index = 0; index < materialized.Length; index++)
        {
            ValidateToken(materialized[index], $"{nameof(arguments)}[{index}]");
        }

        var text = shell switch
        {
            FlowCommandShell.PowerShell => FormatPowerShell(executable, materialized),
            FlowCommandShell.CommandPrompt => FormatCommandPrompt(executable, materialized),
            _ => throw new ArgumentOutOfRangeException(nameof(shell)),
        };
        return new FlowCommandDisplay(shell, executable, materialized, text);
    }

    private static string FormatPowerShell(string executable, ImmutableArray<string> arguments) =>
        "& " + string.Join(' ', new[] { executable }.Concat(arguments).Select(QuotePowerShell));

    private static string QuotePowerShell(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string FormatCommandPrompt(string executable, ImmutableArray<string> arguments) =>
        string.Join(' ', new[] { executable }.Concat(arguments).Select(QuoteCommandPrompt));

    private static string QuoteCommandPrompt(string value)
    {
        var escaped = new StringBuilder(value.Length + 8);
        escaped.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                escaped.Append('\\', (backslashes * 2) + 1);
                escaped.Append('"');
                backslashes = 0;
                continue;
            }

            escaped.Append('\\', backslashes);
            backslashes = 0;
            escaped.Append(character switch
            {
                '%' => "^%",
                '!' => "^!",
                '^' => "^^",
                _ => character.ToString(),
            });
        }

        escaped.Append('\\', backslashes * 2);
        escaped.Append('"');
        return escaped.ToString();
    }

    private static void ValidateToken(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Command display tokens cannot contain nulls or line breaks.", parameterName);
        }
    }
}
