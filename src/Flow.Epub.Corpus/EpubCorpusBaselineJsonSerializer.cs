using System.Buffers;
using System.Text.Json;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Reads and writes the deterministic review baseline for the EPUB corpus.</summary>
public static class EpubCorpusBaselineJsonSerializer
{
    public static byte[] Serialize(EpubCorpusBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubCorpusBaseline.CurrentFormat);
            writer.WriteStartArray("publications");
            foreach (var entry in baseline.Publications)
            {
                WriteEntry(writer, entry);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return AppendLf(buffer.WrittenSpan);
    }

    public static EpubCorpusBaseline Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) || bytes.Contains((byte)'\r'))
        {
            throw new InvalidDataException("The baseline must use UTF-8 without a byte-order mark and LF line endings.");
        }

        using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var root = json.RootElement;
        var format = RequiredString(root, "format");
        if (!string.Equals(format, EpubCorpusBaseline.CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported EPUB corpus baseline format '{format}'.");
        }

        var entries = root.GetProperty("publications").EnumerateArray().Select(ReadEntry).ToArray();
        if (entries.Select(static item => item.Id).Distinct().Count() != entries.Length)
        {
            throw new InvalidDataException("The baseline contains duplicate publication IDs.");
        }

        return new EpubCorpusBaseline(entries);
    }

    public static async Task WriteAtomicallyAsync(
        EpubCorpusBaseline baseline,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The baseline path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, Serialize(baseline), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteEntry(Utf8JsonWriter writer, EpubCorpusBaselineEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("id", entry.Id.Value);
        writer.WriteString("status", Token(entry.Status));
        WriteNullableString(writer, "epubVersion", entry.EpubVersion);
        writer.WriteNumber("manifestItemCount", entry.ManifestItemCount);
        writer.WriteNumber("spineItemCount", entry.SpineItemCount);
        writer.WriteNumber("importedNodeCount", entry.ImportedNodeCount);
        writer.WriteNumber("importedAssetCount", entry.ImportedAssetCount);
        writer.WriteNumber("validationDiagnosticCount", entry.ValidationDiagnosticCount);
        writer.WriteNumber("fidelityLostUnitCount", entry.FidelityLostUnitCount);
        WriteNullableString(writer, "documentId", entry.DocumentId);
        WriteNullableString(writer, "canonicalHash", entry.CanonicalHash);
        writer.WritePropertyName("semantic");
        WriteSemantic(writer, entry.Semantic);
        writer.WriteStartArray("requiredDiagnosticCodes");
        foreach (var code in entry.RequiredDiagnosticCodes)
        {
            writer.WriteStringValue(code);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSemantic(Utf8JsonWriter writer, EpubCorpusSemanticEvidence? semantic)
    {
        if (semantic is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteStartArray("orderedNodeIds");
        foreach (var id in semantic.OrderedNodeIds)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
        writer.WriteNumber("sourceLocationCount", semantic.SourceLocationCount);
        writer.WriteNumber("chapterCount", semantic.ChapterCount);
        writer.WriteNumber("headingCount", semantic.HeadingCount);
        writer.WriteNumber("paragraphCount", semantic.ParagraphCount);
        writer.WriteNumber("tableOfContentsEntryCount", semantic.TableOfContentsEntryCount);
        writer.WriteNumber("internalLinkCount", semantic.InternalLinkCount);
        writer.WriteNumber("figureCount", semantic.FigureCount);
        writer.WriteNumber("footnoteCount", semantic.FootnoteCount);
        writer.WriteNumber("footnoteReferenceCount", semantic.FootnoteReferenceCount);
        writer.WriteNumber("tableCount", semantic.TableCount);
        writer.WriteNumber("tableCellCount", semantic.TableCellCount);
        writer.WriteEndObject();
    }

    private static EpubCorpusBaselineEntry ReadEntry(JsonElement element)
    {
        var status = Enum.Parse<EpubCorpusExecutionStatus>(RequiredString(element, "status"), ignoreCase: true);
        return new EpubCorpusBaselineEntry(
            new EpubCorpusPublicationId(RequiredString(element, "id")),
            status,
            NullableString(element, "epubVersion"),
            element.GetProperty("manifestItemCount").GetInt32(),
            element.GetProperty("spineItemCount").GetInt32(),
            element.GetProperty("importedNodeCount").GetInt32(),
            element.GetProperty("importedAssetCount").GetInt32(),
            element.GetProperty("validationDiagnosticCount").GetInt32(),
            element.GetProperty("fidelityLostUnitCount").GetInt64(),
            NullableString(element, "documentId"),
            NullableString(element, "canonicalHash"),
            ReadSemantic(element.GetProperty("semantic")),
            element.GetProperty("requiredDiagnosticCodes").EnumerateArray().Select(static item => item.GetString()!));
    }

    private static EpubCorpusSemanticEvidence? ReadSemantic(JsonElement element) => element.ValueKind == JsonValueKind.Null
        ? null
        : new EpubCorpusSemanticEvidence(
            element.GetProperty("orderedNodeIds").EnumerateArray().Select(static item => item.GetString()!),
            element.GetProperty("sourceLocationCount").GetInt32(),
            element.GetProperty("chapterCount").GetInt32(),
            element.GetProperty("headingCount").GetInt32(),
            element.GetProperty("paragraphCount").GetInt32(),
            element.GetProperty("tableOfContentsEntryCount").GetInt32(),
            element.GetProperty("internalLinkCount").GetInt32(),
            element.GetProperty("figureCount").GetInt32(),
            element.GetProperty("footnoteCount").GetInt32(),
            element.GetProperty("footnoteReferenceCount").GetInt32(),
            element.GetProperty("tableCount").GetInt32(),
            element.GetProperty("tableCellCount").GetInt32());

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
        ?? throw new InvalidDataException($"Baseline property '{propertyName}' must be a string.");

    private static string? NullableString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static string Token(EpubCorpusExecutionStatus status) => status.ToString().ToLowerInvariant();

    private static byte[] AppendLf(ReadOnlySpan<byte> bytes)
    {
        var result = new ArrayBufferWriter<byte>(bytes.Length + 1);
        foreach (var value in bytes)
        {
            if (value != (byte)'\r')
            {
                result.GetSpan(1)[0] = value;
                result.Advance(1);
            }
        }

        result.GetSpan(1)[0] = (byte)'\n';
        result.Advance(1);
        return result.WrittenSpan.ToArray();
    }
}
