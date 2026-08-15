using System.Diagnostics.CodeAnalysis;

namespace Flow.Core;

/// <summary>Identifies one addressable semantic node independently of pages, coordinates, or layout.</summary>
public sealed record NodeId
{
    /// <summary>Creates a node identifier using the stable Flow identifier grammar.</summary>
    /// <param name="value">The ordinal, lowercase stable identifier.</param>
    public NodeId(string value)
    {
        Value = IdentifierRules.ValidateStableId(value, nameof(value));
    }

    /// <summary>Gets the stable identifier value.</summary>
    public string Value { get; }

    /// <summary>Parses a stable node identifier.</summary>
    /// <param name="value">The identifier text.</param>
    /// <returns>The parsed identifier.</returns>
    public static NodeId Parse(string value) => new(value);

    /// <summary>Attempts to parse a stable node identifier.</summary>
    /// <param name="value">The identifier text.</param>
    /// <param name="nodeId">The parsed identifier when successful.</param>
    /// <returns><see langword="true" /> when the value follows the stable identifier grammar.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out NodeId? nodeId)
    {
        if (value is null)
        {
            nodeId = null;
            return false;
        }

        try
        {
            nodeId = new NodeId(value);
            return true;
        }
        catch (ArgumentException)
        {
            nodeId = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
