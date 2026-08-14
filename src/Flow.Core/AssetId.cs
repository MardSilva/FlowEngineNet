namespace Flow.Core;

public sealed record AssetId
{
    public AssetId(string value)
    {
        Value = IdentifierRules.ValidateStableId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
