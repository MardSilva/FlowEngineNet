using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace Flow.Epub;

internal static class EpubMetadataReader
{
    private static readonly XNamespace OpfNamespace = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace XmlNamespace = XNamespace.Xml;

    internal static EpubMetadataParseResult Read(
        XElement package,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var metadata = package.Element(OpfNamespace + "metadata");
        var allIds = package.DescendantsAndSelf()
            .Select(static element => (string?)element.Attribute("id"))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        var duplicateIds = allIds.GroupBy(static id => id!, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var duplicateId in duplicateIds.OrderBy(static value => value, StringComparer.Ordinal))
        {
            AddWarning(
                diagnostics,
                EpubDiagnosticCodes.MetadataConflict,
                $"Metadata/package ID '{duplicateId}' is duplicated; refinements using it are ambiguous.",
                packagePath);
        }

        var refinements = ReadProperties(metadata, allIds, duplicateIds, packagePath, diagnostics);
        var refinementsByTarget = refinements
            .Where(property => property.Refines is not null
                && property.Refines.StartsWith('#')
                && property.Refines.Length > 1
                && allIds.Contains(property.Refines[1..], StringComparer.Ordinal)
                && !duplicateIds.Contains(property.Refines[1..]))
            .GroupBy(static property => property.Refines![1..], StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToImmutableArray(), StringComparer.Ordinal);

        var uniqueIdentifierId = Normalize((string?)package.Attribute("unique-identifier"));
        var identifiers = ReadIdentifiers(metadata, uniqueIdentifierId, packagePath, diagnostics);
        var selectedIdentifier = SelectIdentifier(identifiers, uniqueIdentifierId, packagePath, diagnostics);

        var titles = ReadTitles(metadata, refinementsByTarget, packagePath, diagnostics);
        var title = SelectTitle(titles, packagePath, diagnostics);
        var subtitle = SelectSubtitle(titles, packagePath, diagnostics);
        var creators = ReadAgents(metadata, refinementsByTarget, DcNamespace + "creator", EpubAgentKind.Creator, packagePath, diagnostics);
        var contributors = ReadAgents(metadata, refinementsByTarget, DcNamespace + "contributor", EpubAgentKind.Contributor, packagePath, diagnostics);
        var authors = creators
            .Where(static creator => creator.Roles.IsEmpty || creator.Roles.Contains("aut", StringComparer.OrdinalIgnoreCase))
            .Select(static creator => creator.Value)
            .ToImmutableArray();
        var languages = ReadValues(metadata, DcNamespace + "language", validateLanguage: true, packagePath, diagnostics);
        var validLanguages = languages.Where(static value => IsValidLanguage(value.Value)).ToArray();
        var publishers = ReadValues(metadata, DcNamespace + "publisher", false, packagePath, diagnostics);
        var descriptions = ReadValues(metadata, DcNamespace + "description", false, packagePath, diagnostics);
        var subjects = ReadValues(metadata, DcNamespace + "subject", false, packagePath, diagnostics);
        var rights = ReadValues(metadata, DcNamespace + "rights", false, packagePath, diagnostics);
        var dates = ReadDates(metadata, packagePath, diagnostics);
        var modified = ReadModified(refinements, packagePath, diagnostics);
        var accessibility = ReadAccessibility(refinements);
        var epub2CoverId = metadata?.Elements(OpfNamespace + "meta")
            .Where(static element => string.Equals((string?)element.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(static element => Normalize((string?)element.Attribute("content")))
            .FirstOrDefault(static value => value is not null);

        var report = new EpubMetadataReport(
            uniqueIdentifierId,
            selectedIdentifier,
            identifiers,
            titles,
            creators,
            contributors,
            publishers,
            languages,
            descriptions,
            subjects,
            dates,
            rights,
            modified,
            accessibility: accessibility,
            properties: refinements);

        return new EpubMetadataParseResult(
            title,
            subtitle,
            selectedIdentifier,
            validLanguages.FirstOrDefault()?.Value,
            authors,
            descriptions.FirstOrDefault()?.Value,
            epub2CoverId,
            report);
    }

    private static ImmutableArray<EpubMetadataProperty> ReadProperties(
        XElement? metadata,
        IReadOnlyCollection<string?> allIds,
        IReadOnlySet<string> duplicateIds,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubMetadataProperty>();
        foreach (var element in metadata?.Elements(OpfNamespace + "meta") ?? [])
        {
            var property = Normalize((string?)element.Attribute("property"));
            if (property is null)
            {
                continue;
            }

            var value = Normalize(element.Value);
            var refines = Normalize((string?)element.Attribute("refines"));
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"Meta property '{property}' has an empty value.", packagePath);
                value = string.Empty;
            }

            if (refines is not null
                && (!refines.StartsWith('#')
                    || refines.Length == 1
                    || !allIds.Contains(refines[1..], StringComparer.Ordinal)
                    || duplicateIds.Contains(refines[1..])))
            {
                AddWarning(
                    diagnostics,
                    EpubDiagnosticCodes.OrphanMetadataRefinement,
                    $"Meta property '{property}' refines unresolved or ambiguous target '{refines}'.",
                    packagePath);
            }

            result.Add(new EpubMetadataProperty(
                property,
                value,
                refines,
                Normalize((string?)element.Attribute("scheme")),
                Normalize((string?)element.Attribute(XmlNamespace + "lang"))));
        }

        return result.ToImmutableArray();
    }

    private static ImmutableArray<EpubIdentifierMetadata> ReadIdentifiers(
        XElement? metadata,
        string? uniqueIdentifierId,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubIdentifierMetadata>();
        foreach (var element in metadata?.Elements(DcNamespace + "identifier") ?? [])
        {
            var value = Normalize(element.Value);
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, "A dc:identifier value is empty.", packagePath);
                continue;
            }

            var id = Normalize((string?)element.Attribute("id"));
            result.Add(new EpubIdentifierMetadata(
                value,
                id,
                Normalize((string?)element.Attribute(OpfNamespace + "scheme")),
                string.Equals(id, uniqueIdentifierId, StringComparison.Ordinal)));
        }

        if (result.Count == 0)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MissingIdentifier, "The OPF package contains no usable dc:identifier.", packagePath);
        }

        return result.ToImmutableArray();
    }

    private static string? SelectIdentifier(
        ImmutableArray<EpubIdentifierMetadata> identifiers,
        string? uniqueIdentifierId,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        if (uniqueIdentifierId is null)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MissingIdentifier, "The OPF package has no unique-identifier attribute.", packagePath);
            return identifiers.FirstOrDefault()?.Value;
        }

        var matches = identifiers.Where(item => string.Equals(item.Id, uniqueIdentifierId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            AddWarning(
                diagnostics,
                matches.Length == 0 ? EpubDiagnosticCodes.MissingIdentifier : EpubDiagnosticCodes.MetadataConflict,
                $"The unique-identifier target '{uniqueIdentifierId}' resolves to {matches.Length} usable identifiers; the first identifier is used as fallback.",
                packagePath);
            return identifiers.FirstOrDefault()?.Value;
        }

        return matches[0].Value;
    }

    private static ImmutableArray<EpubTitleMetadata> ReadTitles(
        XElement? metadata,
        IReadOnlyDictionary<string, ImmutableArray<EpubMetadataProperty>> refinements,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubTitleMetadata>();
        foreach (var element in metadata?.Elements(DcNamespace + "title") ?? [])
        {
            var value = Normalize(element.Value);
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, "A dc:title value is empty.", packagePath);
                continue;
            }

            var id = Normalize((string?)element.Attribute("id"));
            var related = id is not null && refinements.TryGetValue(id, out var values) ? values : [];
            var titleTypes = ValuesFor(related, "title-type").ToArray();
            var sequences = ValuesFor(related, "display-seq").ToArray();
            var fileAs = ValuesFor(related, "file-as").ToArray();
            ReportMultipleRefinements(titleTypes, "title-type", id, packagePath, diagnostics);
            ReportMultipleRefinements(sequences, "display-seq", id, packagePath, diagnostics);
            ReportMultipleRefinements(fileAs, "file-as", id, packagePath, diagnostics);
            int? displaySequence = null;
            if (sequences.FirstOrDefault() is { } sequence)
            {
                if (int.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    displaySequence = parsed;
                }
                else
                {
                    AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"Title display-seq '{sequence}' is invalid.", packagePath);
                }
            }

            result.Add(new EpubTitleMetadata(
                value,
                id,
                titleTypes.FirstOrDefault(),
                displaySequence,
                fileAs.FirstOrDefault(),
                Normalize((string?)element.Attribute(XmlNamespace + "lang"))));
        }

        return result.ToImmutableArray();
    }

    private static string SelectTitle(
        ImmutableArray<EpubTitleMetadata> titles,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var mains = titles.Where(static title => string.Equals(title.TitleType, "main", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (mains.Length > 1)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MetadataConflict, "Multiple titles are refined as the main title; display-seq then source order determines precedence.", packagePath);
        }
        else if (mains.Length == 0 && titles.Length > 1)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MetadataConflict, "Multiple titles exist without a main title refinement; source order determines the canonical title.", packagePath);
        }

        var selected = (mains.Length > 0 ? mains : titles.ToArray())
            .OrderBy(static title => title.DisplaySequence ?? int.MaxValue)
            .FirstOrDefault();
        if (selected is not null)
        {
            return selected.Value;
        }

        AddWarning(diagnostics, EpubDiagnosticCodes.MetadataFallback, "The EPUB has no usable dc:title; the importer used 'Untitled EPUB'.", packagePath);
        return "Untitled EPUB";
    }

    private static string? SelectSubtitle(
        ImmutableArray<EpubTitleMetadata> titles,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var subtitles = titles.Where(static title => string.Equals(title.TitleType, "subtitle", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static title => title.DisplaySequence ?? int.MaxValue)
            .ToArray();
        if (subtitles.Length > 1)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MetadataConflict, "Multiple titles are refined as subtitle; display-seq then source order determines precedence.", packagePath);
        }

        return subtitles.FirstOrDefault()?.Value;
    }

    private static ImmutableArray<EpubAgentMetadata> ReadAgents(
        XElement? metadata,
        IReadOnlyDictionary<string, ImmutableArray<EpubMetadataProperty>> refinements,
        XName name,
        EpubAgentKind kind,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubAgentMetadata>();
        foreach (var element in metadata?.Elements(name) ?? [])
        {
            var value = Normalize(element.Value);
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"A dc:{name.LocalName} value is empty.", packagePath);
                continue;
            }

            var id = Normalize((string?)element.Attribute("id"));
            var related = id is not null && refinements.TryGetValue(id, out var values) ? values : [];
            var roles = ValuesFor(related, "role")
                .Prepend(Normalize((string?)element.Attribute(OpfNamespace + "role")))
                .Where(static role => role is not null)
                .Select(static role => role!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            var refinedFileAs = ValuesFor(related, "file-as").FirstOrDefault();
            result.Add(new EpubAgentMetadata(
                value,
                id,
                kind,
                roles,
                refinedFileAs ?? Normalize((string?)element.Attribute(OpfNamespace + "file-as")),
                Normalize((string?)element.Attribute(XmlNamespace + "lang"))));
        }

        return result.ToImmutableArray();
    }

    private static ImmutableArray<EpubMetadataValue> ReadValues(
        XElement? metadata,
        XName name,
        bool validateLanguage,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubMetadataValue>();
        foreach (var element in metadata?.Elements(name) ?? [])
        {
            var value = Normalize(element.Value);
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"A dc:{name.LocalName} value is empty.", packagePath);
                continue;
            }

            if (validateLanguage && !IsValidLanguage(value))
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidLanguage, $"Language tag '{value}' is not a structurally valid BCP 47 tag.", packagePath);
            }

            result.Add(new EpubMetadataValue(
                value,
                Normalize((string?)element.Attribute("id")),
                Normalize((string?)element.Attribute(XmlNamespace + "lang"))));
        }

        return result.ToImmutableArray();
    }

    private static ImmutableArray<EpubDateMetadata> ReadDates(
        XElement? metadata,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var result = new List<EpubDateMetadata>();
        foreach (var element in metadata?.Elements(DcNamespace + "date") ?? [])
        {
            var value = Normalize(element.Value);
            if (value is null)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, "A dc:date value is empty.", packagePath);
                continue;
            }

            var isValid = IsValidDate(value);
            if (!isValid)
            {
                AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"Date value '{value}' is not a supported ISO-8601 date.", packagePath);
            }

            result.Add(new EpubDateMetadata(value, Normalize((string?)element.Attribute(OpfNamespace + "event")), isValid));
        }

        return result.ToImmutableArray();
    }

    private static string? ReadModified(
        ImmutableArray<EpubMetadataProperty> properties,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var values = properties.Where(static item => string.Equals(item.Property, "dcterms:modified", StringComparison.Ordinal))
            .Select(static item => item.Value)
            .ToArray();
        if (values.Length > 1)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MetadataConflict, "The package declares multiple dcterms:modified values; the first is retained.", packagePath);
        }

        if (values.FirstOrDefault() is { } modified
            && !DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.InvalidMetadata, $"Modified value '{modified}' is not a valid date-time.", packagePath);
        }

        return values.FirstOrDefault();
    }

    private static EpubAccessibilityMetadata ReadAccessibility(ImmutableArray<EpubMetadataProperty> properties) => new(
        ValuesFor(properties, "schema:accessMode"),
        ValuesFor(properties, "schema:accessModeSufficient"),
        ValuesFor(properties, "schema:accessibilityFeature"),
        ValuesFor(properties, "schema:accessibilityHazard"),
        ValuesFor(properties, "schema:accessibilitySummary"));

    private static IEnumerable<string> ValuesFor(IEnumerable<EpubMetadataProperty> properties, string property) =>
        properties.Where(item => string.Equals(item.Property, property, StringComparison.Ordinal))
            .Select(static item => item.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value));

    private static void ReportMultipleRefinements(
        IReadOnlyCollection<string> values,
        string property,
        string? id,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        if (values.Count > 1)
        {
            AddWarning(diagnostics, EpubDiagnosticCodes.MetadataConflict, $"Metadata target '{id}' has multiple '{property}' refinements; the first is retained.", packagePath);
        }
    }

    private static bool IsValidLanguage(string value)
    {
        var parts = value.Split('-');
        if (parts.Length == 0
            || parts[0].Length == 1
            && !string.Equals(parts[0], "x", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parts[0], "i", StringComparison.OrdinalIgnoreCase)
            || parts[0].Length is < 1 or > 8
            || !parts[0].All(IsAsciiLetter)
            || parts[0].Length == 1 && parts.Length == 1)
        {
            return false;
        }

        return parts.Skip(1).All(static part => part.Length is >= 1 and <= 8 && part.All(IsAsciiLetterOrDigit));
    }

    private static bool IsValidDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
        || DateOnly.TryParseExact(value, ["yyyy", "yyyy-MM", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char value) => IsAsciiLetter(value) || value is >= '0' and <= '9';

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AddWarning(
        ICollection<EpubDiagnostic> diagnostics,
        string code,
        string message,
        string resource) =>
        diagnostics.Add(new EpubDiagnostic(code, EpubDiagnosticSeverity.Warning, message, resource));
}

internal sealed record EpubMetadataParseResult(
    string Title,
    string? Subtitle,
    string? Identifier,
    string? Language,
    ImmutableArray<string> Authors,
    string? Description,
    string? Epub2CoverItemId,
    EpubMetadataReport Report);
