using System.Collections.Immutable;

namespace Flow.Documents;

public sealed record DocumentMetadata
{
    public DocumentMetadata(
        string title,
        string? language = null,
        IEnumerable<string>? authors = null,
        string? subtitle = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (language is not null && string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("A language cannot be empty or whitespace.", nameof(language));
        }

        var copiedAuthors = ImmutableCollections.CopyOf(authors ?? [], nameof(authors));
        if (copiedAuthors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Author names cannot be empty or whitespace.", nameof(authors));
        }

        Title = title;
        Language = language;
        Authors = copiedAuthors;
        Subtitle = subtitle;
        Description = description;
    }

    public string Title { get; }

    public string? Language { get; }

    public ImmutableArray<string> Authors { get; }

    public string? Subtitle { get; }

    public string? Description { get; }
}
