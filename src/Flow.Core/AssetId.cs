namespace Flow.Core;

/// <summary>Identifies an immutable document asset independently of its file location or rendering.</summary>
public sealed record AssetId
{
    /// <summary>Creates an asset identifier using the stable Flow identifier grammar.</summary>
    /// <param name="value">The ordinal, lowercase stable identifier.</param>
    public AssetId(string value)
    {
        Value = IdentifierRules.ValidateStableId(value, nameof(value));
    }

    /// <summary>Gets the stable identifier value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
