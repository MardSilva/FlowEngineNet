namespace Flow.Core;

public sealed record DocumentId
{
    public DocumentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 512)
        {
            throw new ArgumentException("A document identifier cannot exceed 512 characters.", nameof(value));
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A document identifier cannot have surrounding whitespace.", nameof(value));
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A document identifier must be an absolute URI.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
