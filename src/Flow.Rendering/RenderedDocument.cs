using System.Collections.Immutable;
using System.Text;

namespace Flow.Rendering;

public sealed record RenderedDocument
{
    public RenderedDocument(
        string mediaType,
        string fileExtension,
        ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);

        if (!fileExtension.StartsWith(".", StringComparison.Ordinal)
            || fileExtension.Length == 1
            || fileExtension.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '.'))
        {
            throw new ArgumentException("A file extension must start with a dot and contain ASCII letters or digits.", nameof(fileExtension));
        }

        MediaType = mediaType;
        FileExtension = fileExtension;
        Content = ImmutableArray.CreateRange(content.ToArray());
    }

    public string MediaType { get; }

    public string FileExtension { get; }

    public ImmutableArray<byte> Content { get; }

    public string ReadAsUtf8() => Encoding.UTF8.GetString(Content.AsSpan());

    public void WriteTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Content.ToArray());
    }
}
