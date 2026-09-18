using System.Text.Json;
using Flow.Epub;

namespace Flow.Epub.Corpus;

public static partial class EpubPrivateQualificationReportJsonSerializer
{
    /// <summary>Reads and validates deterministic private qualification evidence.</summary>
    public static EpubPrivateQualificationReport Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            var root = RequireObject(document.RootElement, "qualification report");
            EnsureUniqueProperties(root, "qualification report");
            if (RequireString(root, "format") != EpubPrivateQualificationReport.CurrentFormat)
            {
                throw new InvalidDataException("The private qualification report format is unsupported.");
            }

            var publicationsElement = RequireProperty(root, "publications");
            if (publicationsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The private qualification publications value must be an array.");
            }

            var publications = publicationsElement.EnumerateArray().Select(ReadPublication).ToArray();
            var duplicate = publications.GroupBy(static item => item.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidDataException($"Private qualification candidate '{duplicate.Key}' occurs more than once.");
            }

            var report = new EpubPrivateQualificationReport(
                RequireBoolean(root, "deterministicAcrossRepeatedRuns"),
                publications);
            ValidateSummary(RequireObject(RequireProperty(root, "summary"), "summary"), report.Summary);
            return report;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The private qualification report is not valid JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The private qualification report contains an invalid typed value.", exception);
        }
    }

    private static EpubPrivateQualificationItem ReadPublication(JsonElement element)
    {
        var item = RequireObject(element, "publication");
        EnsureUniqueProperties(item, "publication");
        var id = new EpubCorpusPublicationId(RequireString(item, "id"));
        var sourceHashElement = RequireProperty(item, "sourceSha256");
        EpubCorpusSha256? sourceHash = sourceHashElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => new EpubCorpusSha256(sourceHashElement.GetString()!),
            _ => throw new InvalidDataException("sourceSha256 must be a string or null."),
        };
        var inventoryStatus = ParseToken<EpubPrivateInventoryStatus>(RequireString(item, "inventoryStatus"));
        var status = ParseToken<EpubPrivateQualificationStatus>(RequireString(item, "status"));
        var eligible = RequireBoolean(item, "eligible");
        var stable = RequireBoolean(item, "stableAcrossRepeatedRuns");
        var phasesElement = RequireProperty(item, "completedPhases");
        if (phasesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("completedPhases must be an array.");
        }

        var phases = phasesElement.EnumerateArray()
            .Select(static value => ParseToken<EpubCorpusExecutionPhase>(RequireStringValue(value, "completed phase")))
            .ToArray();
        var evidence = ReadEvidence(RequireObject(RequireProperty(item, "evidence"), "evidence"));
        var diagnosticsElement = RequireProperty(item, "diagnostics");
        if (diagnosticsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("diagnostics must be an array.");
        }

        var diagnostics = diagnosticsElement.EnumerateArray().Select(ReadDiagnostic).ToArray();
        var skipped = status is EpubPrivateQualificationStatus.SkippedProtected
            or EpubPrivateQualificationStatus.SkippedCorrupt
            or EpubPrivateQualificationStatus.SkippedUnsuitable;
        if (eligible == skipped || eligible && sourceHash is null)
        {
            throw new InvalidDataException($"Candidate '{id}' has inconsistent eligibility evidence.");
        }

        return new EpubPrivateQualificationItem(
            id,
            sourceHash,
            inventoryStatus,
            status,
            eligible,
            stable,
            phases,
            evidence,
            diagnostics);
    }

    private static EpubPrivateQualificationDiagnosticCount ReadDiagnostic(JsonElement element)
    {
        var diagnostic = RequireObject(element, "diagnostic");
        EnsureUniqueProperties(diagnostic, "diagnostic");
        var phaseToken = RequireString(diagnostic, "phase");
        EpubCorpusExecutionPhase? phase = phaseToken == "inventory"
            ? null
            : ParseToken<EpubCorpusExecutionPhase>(phaseToken);
        return new EpubPrivateQualificationDiagnosticCount(
            RequireString(diagnostic, "code"),
            ParseToken<EpubCorpusExecutionDiagnosticSeverity>(RequireString(diagnostic, "severity")),
            phase,
            RequirePositiveInt32(diagnostic, "count"));
    }

    private static EpubPrivateQualificationEvidence ReadEvidence(JsonElement evidence)
    {
        EnsureUniqueProperties(evidence, "evidence");
        var canonicalElement = RequireProperty(evidence, "canonicalHash");
        string? canonicalHash = canonicalElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => new EpubCorpusSha256(canonicalElement.GetString()!).Value,
            _ => throw new InvalidDataException("canonicalHash must be a string or null."),
        };
        return new EpubPrivateQualificationEvidence(
            RequireNonNegativeInt32(evidence, "manifestItemCount"),
            RequireNonNegativeInt32(evidence, "spineItemCount"),
            RequireNonNegativeInt32(evidence, "importedNodeCount"),
            RequireNonNegativeInt32(evidence, "importedAssetCount"),
            RequireNonNegativeInt32(evidence, "validationDiagnosticCount"),
            RequireNonNegativeInt64(evidence, "fidelitySourceUnitCount"),
            RequireNonNegativeInt64(evidence, "fidelityLostUnitCount"),
            canonicalHash,
            RequireNonNegativeInt32(evidence, "flowJsonBytes"),
            RequireNonNegativeInt32(evidence, "mobileLayoutNodeCount"),
            RequireNonNegativeInt32(evidence, "desktopLayoutNodeCount"),
            RequireNonNegativeInt32(evidence, "htmlPackageCount"),
            RequireNonNegativeInt32(evidence, "htmlFileCount"),
            RequireNonNegativeInt64(evidence, "htmlBytes"),
            RequireNonNegativeInt32(evidence, "chapterCount"),
            RequireNonNegativeInt32(evidence, "headingCount"),
            RequireNonNegativeInt32(evidence, "paragraphCount"),
            RequireNonNegativeInt32(evidence, "tableOfContentsEntryCount"),
            RequireNonNegativeInt32(evidence, "internalLinkCount"),
            RequireNonNegativeInt32(evidence, "figureCount"),
            RequireNonNegativeInt32(evidence, "footnoteCount"),
            RequireNonNegativeInt32(evidence, "footnoteReferenceCount"),
            RequireNonNegativeInt32(evidence, "tableCount"),
            RequireNonNegativeInt32(evidence, "tableCellCount"));
    }

    private static void ValidateSummary(JsonElement element, EpubPrivateQualificationSummary expected)
    {
        EnsureUniqueProperties(element, "summary");
        var actual = new EpubPrivateQualificationSummary(
            RequireNonNegativeInt32(element, "total"),
            RequireNonNegativeInt32(element, "eligible"),
            RequireNonNegativeInt32(element, "passed"),
            RequireNonNegativeInt32(element, "failed"),
            RequireNonNegativeInt32(element, "inconclusive"),
            RequireNonNegativeInt32(element, "nondeterministic"),
            RequireNonNegativeInt32(element, "skipped"));
        if (actual != expected)
        {
            throw new InvalidDataException("The private qualification summary does not match its publications.");
        }
    }

    private static T ParseToken<T>(string token)
        where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(Token(value), token, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new InvalidDataException($"Value '{token}' is not a supported {typeof(T).Name} token.");
    }

    private static JsonElement RequireObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            ? element
            : throw new InvalidDataException($"The {name} value must be an object.");

    private static JsonElement RequireProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"Required property '{name}' is missing.");

    private static string RequireString(JsonElement element, string name) =>
        RequireStringValue(RequireProperty(element, name), name);

    private static string RequireStringValue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw new InvalidDataException($"Property '{name}' must be a nonempty string.");

    private static bool RequireBoolean(JsonElement element, string name)
    {
        var value = RequireProperty(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"Property '{name}' must be a boolean."),
        };
    }

    private static int RequirePositiveInt32(JsonElement element, string name)
    {
        var value = RequireNonNegativeInt32(element, name);
        return value > 0 ? value : throw new InvalidDataException($"Property '{name}' must be positive.");
    }

    private static int RequireNonNegativeInt32(JsonElement element, string name)
    {
        var value = RequireProperty(element, name);
        return value.TryGetInt32(out var result) && result >= 0
            ? result
            : throw new InvalidDataException($"Property '{name}' must be a nonnegative 32-bit integer.");
    }

    private static long RequireNonNegativeInt64(JsonElement element, string name)
    {
        var value = RequireProperty(element, name);
        return value.TryGetInt64(out var result) && result >= 0
            ? result
            : throw new InvalidDataException($"Property '{name}' must be a nonnegative 64-bit integer.");
    }

    private static void EnsureUniqueProperties(JsonElement element, string name)
    {
        var duplicate = element.EnumerateObject().GroupBy(static property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"The {name} object repeats property '{duplicate.Key}'.");
        }
    }
}
