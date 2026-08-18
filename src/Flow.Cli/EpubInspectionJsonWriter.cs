using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Epub;

namespace Flow.Cli;

internal static class EpubInspectionJsonWriter
{
    internal static async Task WriteAsync(
        EpubPublicationInspection inspection,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(destination);

        await using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(
                         buffer,
                         new JsonWriterOptions
                         {
                             Indented = true,
                             Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                         }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "flow-epub-inspection-0.1");
            writer.WriteBoolean("success", inspection.IsSuccess);
            writer.WriteString("containerPath", inspection.ContainerPath);
            WritePackage(writer, inspection.Package);
            WriteManifest(writer, inspection.Manifest);
            WriteSpine(writer, inspection.Spine);
            WriteNavigationDocuments(writer, inspection.NavigationDocumentPaths);
            WriteResources(writer, inspection.Resources);
            WriteDiagnostics(writer, inspection.Diagnostics);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var bytes = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        await WriteWithLfLineEndingsAsync(bytes, destination, cancellationToken).ConfigureAwait(false);
    }

    private static void WritePackage(Utf8JsonWriter writer, EpubPackageInfo? package)
    {
        writer.WritePropertyName("package");
        if (package is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("path", package.Path);
        WriteNullableString(writer, "declaredVersion", package.DeclaredVersion);
        writer.WriteString("versionFamily", VersionFamily(package.VersionFamily));
        WriteNullableString(writer, "uniqueIdentifierId", package.UniqueIdentifierId);
        WriteNullableString(writer, "identifier", package.Identifier);
        WriteNullableString(writer, "title", package.Title);
        WriteNullableString(writer, "language", package.Language);
        writer.WritePropertyName("creators");
        writer.WriteStartArray();
        foreach (var creator in package.Creators)
        {
            writer.WriteStringValue(creator);
        }

        writer.WriteEndArray();
        WriteNullableString(writer, "publisher", package.Publisher);
        WriteNullableString(writer, "description", package.Description);
        WriteNullableString(writer, "modified", package.Modified);
        writer.WriteEndObject();
    }

    private static void WriteManifest(Utf8JsonWriter writer, IEnumerable<EpubManifestItemInfo> manifest)
    {
        writer.WritePropertyName("manifest");
        writer.WriteStartArray();
        foreach (var item in manifest.OrderBy(static item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id);
            writer.WriteString("declaredHref", item.DeclaredHref);
            writer.WriteString("path", item.Path);
            writer.WriteString("mediaType", item.MediaType);
            WriteNullableString(writer, "fallbackId", item.FallbackId);
            WriteNullableString(writer, "mediaOverlayId", item.MediaOverlayId);
            writer.WritePropertyName("properties");
            writer.WriteStartArray();
            foreach (var property in item.Properties)
            {
                writer.WriteStringValue(property);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("existsInArchive", item.ExistsInArchive);
            writer.WriteBoolean("navigationDocument", item.IsNavigationDocument);
            writer.WriteBoolean("supported", item.IsSupported);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSpine(Utf8JsonWriter writer, IEnumerable<EpubSpineItemInfo> spine)
    {
        writer.WritePropertyName("spine");
        writer.WriteStartArray();
        foreach (var item in spine.OrderBy(static item => item.Position))
        {
            writer.WriteStartObject();
            writer.WriteNumber("position", item.Position);
            WriteNullableString(writer, "idRef", item.IdRef);
            writer.WriteBoolean("linear", item.IsLinear);
            WriteNullableString(writer, "resourcePath", item.ResourcePath);
            WriteNullableString(writer, "mediaType", item.MediaType);
            writer.WriteBoolean("existsInArchive", item.ExistsInArchive);
            writer.WriteBoolean("supported", item.IsSupported);
            writer.WriteBoolean("repeatedReference", item.IsRepeatedReference);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNavigationDocuments(Utf8JsonWriter writer, IEnumerable<string> paths)
    {
        writer.WritePropertyName("navigationDocuments");
        writer.WriteStartArray();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(path);
        }

        writer.WriteEndArray();
    }

    private static void WriteResources(Utf8JsonWriter writer, EpubResourceSummary resources)
    {
        writer.WritePropertyName("resources");
        writer.WriteStartObject();
        writer.WriteNumber("archiveEntryCount", resources.ArchiveEntryCount);
        writer.WriteNumber("manifestItemCount", resources.ManifestItemCount);
        writer.WriteNumber("existingManifestItemCount", resources.ExistingManifestItemCount);
        writer.WriteNumber("missingManifestItemCount", resources.MissingManifestItemCount);
        writer.WriteNumber("unsupportedManifestItemCount", resources.UnsupportedManifestItemCount);
        writer.WriteNumber("navigationDocumentCount", resources.NavigationDocumentCount);
        writer.WriteNumber("totalCompressedBytes", resources.TotalCompressedBytes);
        writer.WriteNumber("totalUncompressedBytes", resources.TotalUncompressedBytes);
        writer.WritePropertyName("mediaTypeCounts");
        writer.WriteStartObject();
        foreach (var (mediaType, count) in resources.MediaTypeCounts)
        {
            writer.WriteNumber(mediaType, count);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IEnumerable<EpubDiagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            writer.WriteString("message", diagnostic.Message);
            WriteNullableString(writer, "resource", diagnostic.Resource);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value);
    }

    private static string VersionFamily(EpubVersionFamily version) => version switch
    {
        EpubVersionFamily.Unknown => "unknown",
        EpubVersionFamily.Epub2 => "epub2",
        EpubVersionFamily.Epub3 => "epub3",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EPUB version family."),
    };

    private static async Task WriteWithLfLineEndingsAsync(
        ReadOnlyMemory<byte> source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (source.Span.IndexOf((byte)'\r') < 0)
        {
            await destination.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalized = new byte[source.Length];
        var written = 0;
        var bytes = source.Span;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == '\r' && index + 1 < bytes.Length && bytes[index + 1] == '\n')
            {
                normalized[written++] = (byte)'\n';
                index++;
                continue;
            }

            normalized[written++] = bytes[index];
        }

        await destination.WriteAsync(normalized.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
    }
}
