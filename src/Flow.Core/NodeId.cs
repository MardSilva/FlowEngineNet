using System.Diagnostics.CodeAnalysis;

namespace Flow.Core;

public sealed record NodeId
{
    public NodeId(string value)
    {
        Value = IdentifierRules.ValidateStableId(value, nameof(value));
    }

    public string Value { get; }

    public static NodeId Parse(string value) => new(value);

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

    public override string ToString() => Value;
}
