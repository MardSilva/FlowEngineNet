using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Epub;

namespace Flow.Cli;

internal static class EpubImportEvidenceJsonWriter
{
    internal const string MetadataFormat = "flow-epub-metadata-0.1";
    internal const string ProcessingFormat = "flow-epub-package-processing-0.1";
    internal const string SourceMapFormat = "flow-epub-source-map-0.1";

    private const string Scope =
        "Noncanonical EPUB import evidence; this report does not change FlowDocument or its hash.";

    internal static Task WriteMetadataAsync(
        EpubImportResult import,
        Stream destination,
        CancellationToken cancellationToken) =>
        WriteAsync(
            import,
            destination,
            MetadataFormat,
            "metadata",
            import.MetadataReport,
            WriteMetadata,
            cancellationToken);

    internal static Task WriteProcessingAsync(
        EpubImportResult import,
        Stream destination,
        CancellationToken cancellationToken) =>
        WriteAsync(
            import,
            destination,
            ProcessingFormat,
            "processing",
            import.ProcessingReport,
            WriteProcessing,
            cancellationToken);

    internal static Task WriteSourceMapAsync(
        EpubImportResult import,
        Stream destination,
        CancellationToken cancellationToken) =>
        WriteAsync(
            import,
            destination,
            SourceMapFormat,
            "sourceMap",
            import.SourceMap,
            (writer, sourceMap) => WriteSourceMap(writer, sourceMap, cancellationToken),
            cancellationToken);

    private static async Task WriteAsync<T>(
        EpubImportResult import,
        Stream destination,
        string format,
        string payloadName,
        T? payload,
        Action<Utf8JsonWriter, T> writePayload,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(destination);

        await using var normalizedDestination = new LfNormalizingWriteStream(destination);
        await using var writer = new Utf8JsonWriter(
            normalizedDestination,
            new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            });
        writer.WriteStartObject();
        writer.WriteString("format", format);
        writer.WriteString("scope", Scope);
        writer.WriteBoolean("importSucceeded", import.IsSuccess && import.Document is not null);
        WriteNullableString(writer, "documentId", import.Document?.Identity.Id.Value);
        writer.WriteBoolean("available", payload is not null);
        writer.WritePropertyName(payloadName);
        cancellationToken.ThrowIfCancellationRequested();
        if (payload is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writePayload(writer, payload);
        }

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteMetadata(Utf8JsonWriter writer, EpubMetadataReport report)
    {
        writer.WriteStartObject();
        WriteNullableString(writer, "uniqueIdentifierId", report.UniqueIdentifierId);
        WriteNullableString(writer, "selectedIdentifier", report.SelectedIdentifier);
        WriteArray(writer, "identifiers", report.Identifiers, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("value", value.Value);
            WriteNullableString(json, "id", value.Id);
            WriteNullableString(json, "scheme", value.Scheme);
            json.WriteBoolean("isUnique", value.IsUnique);
            json.WriteEndObject();
        });
        WriteArray(writer, "titles", report.Titles, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("value", value.Value);
            WriteNullableString(json, "id", value.Id);
            WriteNullableString(json, "titleType", value.TitleType);
            WriteNullableNumber(json, "displaySequence", value.DisplaySequence);
            WriteNullableString(json, "fileAs", value.FileAs);
            WriteNullableString(json, "language", value.Language);
            json.WriteEndObject();
        });
        WriteAgents(writer, "creators", report.Creators);
        WriteAgents(writer, "contributors", report.Contributors);
        WriteMetadataValues(writer, "publishers", report.Publishers);
        WriteMetadataValues(writer, "languages", report.Languages);
        WriteMetadataValues(writer, "descriptions", report.Descriptions);
        WriteMetadataValues(writer, "subjects", report.Subjects);
        WriteArray(writer, "dates", report.Dates, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("value", value.Value);
            WriteNullableString(json, "event", value.Event);
            json.WriteBoolean("isValid", value.IsValid);
            json.WriteEndObject();
        });
        WriteMetadataValues(writer, "rights", report.Rights);
        WriteNullableString(writer, "modified", report.Modified);
        WriteCover(writer, report.Cover);
        WriteAccessibility(writer, report.Accessibility);
        WriteArray(writer, "properties", report.Properties, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("property", value.Property);
            json.WriteString("value", value.Value);
            WriteNullableString(json, "refines", value.Refines);
            WriteNullableString(json, "scheme", value.Scheme);
            WriteNullableString(json, "language", value.Language);
            json.WriteEndObject();
        });
        writer.WriteEndObject();
    }

    private static void WriteProcessing(Utf8JsonWriter writer, EpubPackageProcessingReport report)
    {
        writer.WriteStartObject();
        WriteArray(writer, "manifest", report.Manifest, static (json, item) =>
        {
            json.WriteStartObject();
            json.WriteString("id", item.Id);
            json.WriteString("path", item.Path);
            json.WriteString("mediaType", item.MediaType);
            WriteStrings(json, "properties", item.Properties);
            WriteNullableString(json, "fallbackId", item.FallbackId);
            WriteNullableString(json, "mediaOverlayId", item.MediaOverlayId);
            json.WriteBoolean("existsInArchive", item.ExistsInArchive);
            json.WriteEndObject();
        });
        WriteArray(writer, "spine", report.Spine, static (json, item) =>
        {
            json.WriteStartObject();
            json.WriteNumber("position", item.Position);
            WriteNullableString(json, "requestedItemId", item.RequestedItemId);
            json.WriteString("readingRole", CamelCase(item.ReadingRole.ToString()));
            json.WriteBoolean("isRepeatedReference", item.IsRepeatedReference);
            json.WriteString("disposition", CamelCase(item.Disposition.ToString()));
            json.WriteString("reason", CamelCase(item.Reason.ToString()));
            WriteNullableString(json, "selectedItemId", item.SelectedItemId);
            WriteNullableString(json, "selectedResourcePath", item.SelectedResourcePath);
            WriteStrings(json, "fallbackChain", item.FallbackChain);
            json.WriteEndObject();
        });
        writer.WriteEndObject();
    }

    private static void WriteSourceMap(
        Utf8JsonWriter writer,
        EpubSourceMap sourceMap,
        CancellationToken cancellationToken)
    {
        writer.WriteStartObject();
        writer.WriteNumber("locationCount", sourceMap.Locations.Length);
        writer.WriteStartArray("locations");
        foreach (var location in sourceMap.Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            writer.WriteString("resourcePath", location.ResourcePath);
            WriteNullableString(writer, "fragment", location.Fragment);
            writer.WriteString("nodeId", location.NodeId.Value);
            writer.WriteNumber("occurrence", location.Occurrence);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteAgents(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<EpubAgentMetadata> values) =>
        WriteArray(writer, name, values, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("value", value.Value);
            WriteNullableString(json, "id", value.Id);
            json.WriteString("kind", CamelCase(value.Kind.ToString()));
            WriteStrings(json, "roles", value.Roles);
            WriteNullableString(json, "fileAs", value.FileAs);
            WriteNullableString(json, "language", value.Language);
            json.WriteEndObject();
        });

    private static void WriteMetadataValues(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<EpubMetadataValue> values) =>
        WriteArray(writer, name, values, static (json, value) =>
        {
            json.WriteStartObject();
            json.WriteString("value", value.Value);
            WriteNullableString(json, "id", value.Id);
            WriteNullableString(json, "language", value.Language);
            json.WriteEndObject();
        });

    private static void WriteCover(Utf8JsonWriter writer, EpubCoverMetadata? cover)
    {
        writer.WritePropertyName("cover");
        if (cover is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("itemId", cover.ItemId);
        writer.WriteString("path", cover.Path);
        writer.WriteString("mediaType", cover.MediaType);
        writer.WriteString("source", cover.Source);
        WriteNullableString(writer, "assetId", cover.AssetId?.Value);
        writer.WriteEndObject();
    }

    private static void WriteAccessibility(Utf8JsonWriter writer, EpubAccessibilityMetadata accessibility)
    {
        writer.WritePropertyName("accessibility");
        writer.WriteStartObject();
        WriteStrings(writer, "accessModes", accessibility.AccessModes);
        WriteStrings(writer, "accessModeSufficient", accessibility.AccessModeSufficient);
        WriteStrings(writer, "features", accessibility.Features);
        WriteStrings(writer, "hazards", accessibility.Hazards);
        WriteStrings(writer, "summaries", accessibility.Summaries);
        writer.WriteEndObject();
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<T> values,
        Action<Utf8JsonWriter, T> writeValue)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writeValue(writer, value);
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values) =>
        WriteArray(writer, name, values, static (json, value) => json.WriteStringValue(value));

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static string CamelCase(string value) => char.ToLowerInvariant(value[0]) + value[1..];
}
