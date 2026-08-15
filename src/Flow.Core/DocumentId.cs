namespace Flow.Core;

/// <summary>Identifies a Flow document with an absolute URI that is independent of any rendition.</summary>
public sealed record DocumentId
{
    /// <summary>Creates a document identifier.</summary>
    /// <param name="value">An absolute URI with no surrounding whitespace.</param>
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

    /// <summary>Gets the absolute URI value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
