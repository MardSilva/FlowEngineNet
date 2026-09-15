using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Documents;

namespace Flow.Epub;

/// <summary>Reads and writes the deterministic flow-epub-corpus-0.1 JSON format.</summary>
public sealed class EpubCorpusManifestJsonSerializer
{
    public const int MaximumManifestBytes = 1024 * 1024;

    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "format",
        "publications",
    };

    private static readonly HashSet<string> PublicationProperties = new(StringComparer.Ordinal)
    {
        "id",
        "title",
        "origin",
        "license",
        "kind",
        "redistribution",
        "relativePath",
        "expectedEpubVersion",
        "expectedSizeBytes",
        "languages",
        "expectedResources",
        "expectedFeatures",
        "expectedResults",
        "knownLimitations",
        "sha256",
    };

    private static readonly HashSet<string> LicenseProperties = new(StringComparer.Ordinal)
    {
        "name",
        "evidence",
    };

    public EpubCorpusManifestReadResult Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The corpus manifest stream must be readable.", nameof(source));
        }

        var diagnostics = new List<EpubCorpusDiagnostic>();
        var bytes = ReadBounded(source, diagnostics);
        if (bytes is null)
        {
            return new EpubCorpusManifestReadResult(null, diagnostics);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) || bytes.Contains((byte)'\r'))
        {
            diagnostics.Add(Error(
                EpubCorpusDiagnosticCodes.InvalidEncoding,
                "The corpus manifest must use UTF-8 without a byte-order mark and LF line endings.",
                "$"));
        }

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error(
                EpubCorpusDiagnosticCodes.InvalidJson,
                $"Invalid corpus manifest JSON at line {exception.LineNumber ?? 0}, byte {exception.BytePositionInLine ?? 0}.",
                "$"));
            return new EpubCorpusManifestReadResult(null, diagnostics);
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidJson, "The corpus manifest root must be an object.", "$"));
                return new EpubCorpusManifestReadResult(null, diagnostics);
            }

            ValidateProperties(root, RootProperties, "$", diagnostics);
            var format = ReadRequiredString(root, "format", "$", diagnostics);
            if (format is not null && !string.Equals(format, EpubCorpusManifest.CurrentFormat, StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    EpubCorpusDiagnosticCodes.UnsupportedFormat,
                    $"Unsupported corpus manifest format '{format}'. Expected '{EpubCorpusManifest.CurrentFormat}'.",
                    "$.format"));
            }

            var publications = ReadPublications(root, diagnostics);
            AddDuplicateIdDiagnostics(publications, diagnostics);

            if (diagnostics.Any(static item => item.Severity == EpubCorpusDiagnosticSeverity.Error))
            {
                return new EpubCorpusManifestReadResult(null, diagnostics);
            }

            return new EpubCorpusManifestReadResult(
                new EpubCorpusManifest(format!, publications.Select(static item => item.Publication!)),
                diagnostics);
        }
    }

    public void Write(EpubCorpusManifest manifest, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The corpus manifest stream must be writable.", nameof(destination));
        }

        var validation = Validate(manifest);
        if (!validation.IsSuccess)
        {
            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                validation.Diagnostics.Select(static item => $"{item.Code}: {item.Message}")));
        }

        WriteUnchecked(manifest, destination);
    }

    public EpubCorpusManifestReadResult Validate(EpubCorpusManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        using var buffer = new MemoryStream();
        WriteUnchecked(manifest, buffer);
        buffer.Position = 0;
        return Read(buffer);
    }

    private static void WriteUnchecked(EpubCorpusManifest manifest, Stream destination)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Indented = true,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", manifest.Format);
            writer.WriteStartArray("publications");
            foreach (var publication in manifest.Publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal))
            {
                WritePublication(writer, publication);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        foreach (var value in buffer.WrittenSpan)
        {
            if (value != (byte)'\r')
            {
                destination.WriteByte(value);
            }
        }

        destination.WriteByte((byte)'\n');
    }

    private static void WritePublication(Utf8JsonWriter writer, EpubCorpusPublication publication)
    {
        writer.WriteStartObject();
        writer.WriteString("id", publication.Id.Value);
        writer.WriteString("title", publication.Title);
        writer.WriteString("origin", publication.Origin);
        writer.WriteStartObject("license");
        writer.WriteString("name", publication.License.Name);
        writer.WriteString("evidence", publication.License.Evidence);
        writer.WriteEndObject();
        writer.WriteString("kind", WriteKind(publication.Kind));
        writer.WriteString("redistribution", WriteRedistribution(publication.Redistribution));
        writer.WriteString("relativePath", publication.RelativePath);
        writer.WriteString("expectedEpubVersion", WriteVersion(publication.ExpectedEpubVersion));
        writer.WriteNumber("expectedSizeBytes", publication.ExpectedSizeBytes);
        WriteStringArray(writer, "languages", publication.Languages);
        WriteStringArray(writer, "expectedResources", publication.ExpectedResources.Order(StringComparer.Ordinal));
        WriteStringArray(writer, "expectedFeatures", publication.ExpectedFeatures.Order(StringComparer.Ordinal));
        WriteStringArray(writer, "expectedResults", publication.ExpectedResults.Order(StringComparer.Ordinal));
        WriteStringArray(writer, "knownLimitations", publication.KnownLimitations.Order(StringComparer.Ordinal));
        writer.WriteString("sha256", publication.Sha256.Value);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static List<ParsedPublication> ReadPublications(
        JsonElement root,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("publications", out var element))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, "Required property 'publications' is missing.", "$.publications"));
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, "Property 'publications' must be an array.", "$.publications"));
            return [];
        }

        var result = new List<ParsedPublication>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var resource = $"$.publications[{index}]";
            result.Add(ReadPublication(item, resource, diagnostics));
            index++;
        }

        return result;
    }

    private static ParsedPublication ReadPublication(
        JsonElement element,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, "A publication entry must be an object.", resource));
            return new ParsedPublication(null, null);
        }

        ValidateProperties(element, PublicationProperties, resource, diagnostics);
        var rawId = ReadRequiredString(element, "id", resource, diagnostics);
        EpubCorpusPublicationId? id = null;
        if (rawId is not null)
        {
            if (EpubCorpusPublicationId.TryParse(rawId, out var parsed))
            {
                id = parsed;
            }
            else
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidPublicationId, "The publication ID is invalid.", $"{resource}.id"));
            }
        }

        var title = ReadRequiredString(element, "title", resource, diagnostics);
        var origin = ReadRequiredString(element, "origin", resource, diagnostics);
        var license = ReadLicense(element, resource, diagnostics);
        var kind = ReadKind(element, resource, diagnostics);
        var redistribution = ReadRedistribution(element, resource, diagnostics);
        var relativePath = ReadRequiredString(element, "relativePath", resource, diagnostics);
        if (relativePath is not null && !IsSafeRelativePath(relativePath))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.UnsafeRelativePath, "The publication path must be a safe forward-slash relative path without dot segments.", $"{resource}.relativePath"));
        }

        var version = ReadVersion(element, resource, diagnostics);
        var expectedSize = ReadPositiveInt64(element, "expectedSizeBytes", resource, diagnostics);
        var languages = ReadStringArray(element, "languages", resource, diagnostics, requireAtLeastOne: true);
        foreach (var (language, languageIndex) in languages.Select(static (value, index) => (value, index)))
        {
            if (!LanguageTag.TryParse(language, out _))
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Language '{language}' is not a structurally valid language tag.", $"{resource}.languages[{languageIndex}]"));
            }
        }

        var expectedResources = ReadTokenArray(element, "expectedResources", resource, diagnostics);
        var features = ReadTokenArray(element, "expectedFeatures", resource, diagnostics);
        var expectedResults = ReadTokenArray(element, "expectedResults", resource, diagnostics);
        var limitations = ReadStringArray(element, "knownLimitations", resource, diagnostics, requireAtLeastOne: false);
        var rawHash = ReadRequiredString(element, "sha256", resource, diagnostics);
        EpubCorpusSha256? hash = null;
        if (rawHash is not null)
        {
            if (EpubCorpusSha256.TryParse(rawHash, out var parsed))
            {
                hash = parsed;
            }
            else
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidSha256, "The SHA-256 value must contain exactly 64 hexadecimal characters.", $"{resource}.sha256"));
            }
        }

        if (kind is not null && redistribution is not null && !IsLicenseCombinationValid(kind.Value, redistribution.Value))
        {
            diagnostics.Add(Error(
                EpubCorpusDiagnosticCodes.InvalidLicense,
                "Project fixtures and redistributable publications must allow redistribution; local non-redistributable publications must prohibit it.",
                $"{resource}.redistribution"));
        }

        var canConstruct = id is not null
            && title is not null
            && origin is not null
            && license is not null
            && kind is not null
            && redistribution is not null
            && relativePath is not null
            && IsSafeRelativePath(relativePath)
            && version is not null
            && expectedSize is not null
            && languages.Count > 0
            && languages.All(static value => LanguageTag.TryParse(value, out _))
            && rawHash is not null
            && hash is not null
            && IsLicenseCombinationValid(kind.Value, redistribution.Value);

        var publication = canConstruct
            ? new EpubCorpusPublication(
                id!.Value,
                title!,
                origin!,
                license!,
                kind!.Value,
                redistribution!.Value,
                relativePath!,
                version!.Value,
                expectedSize!.Value,
                languages,
                expectedResources,
                features,
                expectedResults,
                limitations,
                hash!.Value)
            : null;
        return new ParsedPublication(rawId, publication);
    }

    private static EpubCorpusLicense? ReadLicense(
        JsonElement publication,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        if (!publication.TryGetProperty("license", out var element))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, "Required property 'license' is missing.", $"{resource}.license"));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidLicense, "Property 'license' must be an object.", $"{resource}.license"));
            return null;
        }

        ValidateProperties(element, LicenseProperties, $"{resource}.license", diagnostics);
        var name = ReadRequiredString(element, "name", $"{resource}.license", diagnostics);
        var evidence = ReadRequiredString(element, "evidence", $"{resource}.license", diagnostics);
        return name is not null && evidence is not null ? new EpubCorpusLicense(name, evidence) : null;
    }

    private static EpubCorpusPublicationKind? ReadKind(JsonElement element, string resource, ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var value = ReadRequiredString(element, "kind", resource, diagnostics);
        return value switch
        {
            "projectFixture" => EpubCorpusPublicationKind.ProjectFixture,
            "redistributablePublication" => EpubCorpusPublicationKind.RedistributablePublication,
            "localNonRedistributablePublication" => EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            null => null,
            _ => AddInvalidEnum<EpubCorpusPublicationKind>(diagnostics, $"{resource}.kind", "publication kind", value),
        };
    }

    private static EpubCorpusRedistribution? ReadRedistribution(JsonElement element, string resource, ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var value = ReadRequiredString(element, "redistribution", resource, diagnostics);
        return value switch
        {
            "allowed" => EpubCorpusRedistribution.Allowed,
            "prohibited" => EpubCorpusRedistribution.Prohibited,
            null => null,
            _ => AddInvalidEnum<EpubCorpusRedistribution>(diagnostics, $"{resource}.redistribution", "redistribution value", value),
        };
    }

    private static EpubVersionFamily? ReadVersion(JsonElement element, string resource, ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var value = ReadRequiredString(element, "expectedEpubVersion", resource, diagnostics);
        return value switch
        {
            "epub2" => EpubVersionFamily.Epub2,
            "epub3" => EpubVersionFamily.Epub3,
            null => null,
            _ => AddUnsupportedVersion(diagnostics, resource, value),
        };
    }

    private static T? AddInvalidEnum<T>(ICollection<EpubCorpusDiagnostic> diagnostics, string resource, string description, string value)
        where T : struct
    {
        diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Unknown {description} '{value}'.", resource));
        return null;
    }

    private static EpubVersionFamily? AddUnsupportedVersion(ICollection<EpubCorpusDiagnostic> diagnostics, string resource, string value)
    {
        diagnostics.Add(Error(EpubCorpusDiagnosticCodes.UnsupportedEpubVersion, $"Unsupported expected EPUB version '{value}'.", $"{resource}.expectedEpubVersion"));
        return null;
    }

    private static string? ReadRequiredString(
        JsonElement element,
        string propertyName,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var propertyResource = $"{resource}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, $"Required property '{propertyName}' is missing.", propertyResource));
            return null;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, $"Required property '{propertyName}' must be a non-empty string.", propertyResource));
            return null;
        }

        return property.GetString();
    }

    private static long? ReadPositiveInt64(
        JsonElement element,
        string propertyName,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var propertyResource = $"{resource}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, $"Required property '{propertyName}' is missing.", propertyResource));
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value) || value <= 0)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Property '{propertyName}' must be a positive 64-bit integer.", propertyResource));
            return null;
        }

        return value;
    }

    private static List<string> ReadTokenArray(
        JsonElement element,
        string propertyName,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var values = ReadStringArray(element, propertyName, resource, diagnostics, requireAtLeastOne: false);
        for (var index = 0; index < values.Count; index++)
        {
            if (!IsToken(values[index]))
            {
                diagnostics.Add(Error(
                    EpubCorpusDiagnosticCodes.InvalidValue,
                    $"Value '{values[index]}' must be a lowercase corpus token containing letters, digits, dots, hyphens, or underscores.",
                    $"{resource}.{propertyName}[{index}]"));
            }
        }

        return values;
    }

    private static List<string> ReadStringArray(
        JsonElement element,
        string propertyName,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics,
        bool requireAtLeastOne)
    {
        var propertyResource = $"{resource}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.MissingRequiredValue, $"Required property '{propertyName}' is missing.", propertyResource));
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Property '{propertyName}' must be an array.", propertyResource));
            return [];
        }

        var values = new List<string>();
        var index = 0;
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Every '{propertyName}' value must be a non-empty string.", $"{propertyResource}[{index}]"));
            }
            else
            {
                values.Add(item.GetString()!);
            }

            index++;
        }

        if (requireAtLeastOne && values.Count == 0)
        {
            diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"Property '{propertyName}' must contain at least one value.", propertyResource));
        }

        return values;
    }

    private static void ValidateProperties(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string resource,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidJson, $"Property '{property.Name}' is duplicated.", $"{resource}.{property.Name}"));
            }
            else if (!allowed.Contains(property.Name))
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.UnknownProperty, $"Unknown property '{property.Name}'.", $"{resource}.{property.Name}"));
            }
        }
    }

    private static void AddDuplicateIdDiagnostics(
        IEnumerable<ParsedPublication> publications,
        ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        foreach (var duplicate in publications
            .Where(static item => item.RawId is not null)
            .GroupBy(static item => item.RawId!, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(
                EpubCorpusDiagnosticCodes.DuplicatePublicationId,
                $"Publication ID '{duplicate.Key}' occurs {duplicate.Count()} times.",
                $"publications/{duplicate.Key}"));
        }
    }

    private static byte[]? ReadBounded(Stream source, ICollection<EpubCorpusDiagnostic> diagnostics)
    {
        var writer = new ArrayBufferWriter<byte>();
        Span<byte> buffer = stackalloc byte[4096];
        while (true)
        {
            var read = source.Read(buffer);
            if (read == 0)
            {
                break;
            }

            if (writer.WrittenCount + read > MaximumManifestBytes)
            {
                diagnostics.Add(Error(EpubCorpusDiagnosticCodes.InvalidValue, $"The corpus manifest exceeds the {MaximumManifestBytes}-byte limit.", "$"));
                return null;
            }

            writer.Write(buffer[..read]);
        }

        return writer.WrittenSpan.ToArray();
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith('/')
            || value.Contains('\\')
            || value.Contains(':')
            || value.Any(static character => char.IsControl(character))
            || !HasValidPercentEncoding(value))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(value);
        if (decoded.StartsWith('/')
            || decoded.Contains('\\')
            || decoded.Contains(':')
            || decoded.Any(static character => char.IsControl(character)))
        {
            return false;
        }

        var segments = decoded.Split('/', StringSplitOptions.None);
        return segments.All(static segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool HasValidPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }

            index += 2;
        }

        return true;
    }

    private static bool IsToken(string value) => value.Length is > 0 and <= 64
        && IsTokenCharacter(value[0], first: true)
        && value.All(static character => IsTokenCharacter(character, first: false));

    private static bool IsTokenCharacter(char value, bool first) => value is >= 'a' and <= 'z' or >= '0' and <= '9'
        || (!first && value is '.' or '-' or '_');

    private static bool IsLicenseCombinationValid(EpubCorpusPublicationKind kind, EpubCorpusRedistribution redistribution) =>
        kind == EpubCorpusPublicationKind.LocalNonRedistributablePublication
            ? redistribution == EpubCorpusRedistribution.Prohibited
            : redistribution == EpubCorpusRedistribution.Allowed;

    private static string WriteKind(EpubCorpusPublicationKind kind) => kind switch
    {
        EpubCorpusPublicationKind.ProjectFixture => "projectFixture",
        EpubCorpusPublicationKind.RedistributablePublication => "redistributablePublication",
        EpubCorpusPublicationKind.LocalNonRedistributablePublication => "localNonRedistributablePublication",
        _ => "unknown",
    };

    private static string WriteRedistribution(EpubCorpusRedistribution redistribution) => redistribution switch
    {
        EpubCorpusRedistribution.Allowed => "allowed",
        EpubCorpusRedistribution.Prohibited => "prohibited",
        _ => "unknown",
    };

    private static string WriteVersion(EpubVersionFamily version) => version switch
    {
        EpubVersionFamily.Epub2 => "epub2",
        EpubVersionFamily.Epub3 => "epub3",
        _ => "unknown",
    };

    private static EpubCorpusDiagnostic Error(string code, string message, string resource) =>
        new(code, EpubCorpusDiagnosticSeverity.Error, message, resource);

    private sealed record ParsedPublication(string? RawId, EpubCorpusPublication? Publication);
}
