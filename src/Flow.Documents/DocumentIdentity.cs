using Flow.Core;

namespace Flow.Documents;

public sealed record DocumentIdentity
{
    public DocumentIdentity(DocumentId id, string? version = null, DocumentId? previousVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (version is not null && string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A version cannot be empty or whitespace.", nameof(version));
        }

        Id = id;
        Version = version;
        PreviousVersionId = previousVersionId;
    }

    public DocumentId Id { get; }

    public string? Version { get; }

    public DocumentId? PreviousVersionId { get; }
}
