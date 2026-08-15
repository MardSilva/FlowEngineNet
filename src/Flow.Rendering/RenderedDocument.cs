using System.Collections.Immutable;
using System.Text;

namespace Flow.Rendering;

/// <summary>Contains immutable bytes and media metadata produced by a renderer.</summary>
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

    /// <summary>Decodes the rendered content as UTF-8 text.</summary>
    public string ReadAsUtf8() => Encoding.UTF8.GetString(Content.AsSpan());

    /// <summary>Writes the immutable rendered bytes to a file, replacing an existing target.</summary>
    public void WriteTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Content.ToArray());
    }
}
