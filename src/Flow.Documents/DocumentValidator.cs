using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

/// <summary>Validates document-wide semantic, hierarchy, asset, and reference invariants.</summary>
public sealed class DocumentValidator
{
    /// <summary>Validates a complete immutable document without modifying it.</summary>
    /// <param name="document">The document to validate.</param>
    /// <returns>All diagnostics found in deterministic validation-pass order.</returns>
    public ValidationResult Validate(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = ImmutableArray.CreateBuilder<ValidationDiagnostic>();

        ValidateIds(document, diagnostics);
        ValidateHierarchy(document, diagnostics);
        ValidateHeadingLevels(document, diagnostics);
        ValidateFigures(document, diagnostics);
        ValidateReferences(document, diagnostics);
        ValidateTableOfContents(document, diagnostics);

        return new ValidationResult(diagnostics);
    }

    private static void ValidateIds(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            if (!NodeId.TryParse(location.Node.Id.Value, out _))
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidNodeId,
                    $"Node ID '{location.Node.Id.Value}' is invalid.",
                    location.Node.Id));
            }
        }

        foreach (var duplicateId in document.Index.DuplicateIds)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.DuplicateNodeId,
                $"Node ID '{duplicateId}' occurs {document.Index.GetLocations(duplicateId).Length} times.",
                duplicateId));
        }
    }

    private static void ValidateHierarchy(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            var isValid = location.Node switch
            {
                Chapter => location.Parent is null,
                Section => location.Parent is Chapter or Section,
                ListItem => location.Parent is OrderedList or UnorderedList,
                Caption => location.Parent is Figure,
                Footnote when location.Parent is Footnote => false,
                _ => true,
            };

            if (!isValid)
            {
                var parentName = location.Parent?.GetType().Name ?? "the document root";
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHierarchy,
                    $"{location.Node.GetType().Name} '{location.Node.Id}' is not valid inside {parentName}.",
                    location.Node.Id));
            }
        }
    }

    private static void ValidateHeadingLevels(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        Heading? previousHeading = null;

        foreach (var heading in document.Index.Locations.Select(static location => location.Node).OfType<Heading>())
        {
            if (heading.Level is < 1 or > 6)
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHeadingLevel,
                    $"Heading '{heading.Id}' has level {heading.Level}; valid levels are 1 through 6.",
                    heading.Id));
            }
            else if (previousHeading is not null && heading.Level > previousHeading.Level + 1)
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHeadingLevel,
                    $"Heading '{heading.Id}' jumps from level {previousHeading.Level} to level {heading.Level}.",
                    heading.Id));
            }

            previousHeading = heading;
        }
    }

    private static void ValidateFigures(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var figure in document.Index.Locations.Select(static location => location.Node).OfType<Figure>())
        {
            if (!document.Assets.ContainsKey(figure.AssetId))
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.MissingFigureAsset,
                    $"Figure '{figure.Id}' references missing asset '{figure.AssetId}'.",
                    figure.Id));
            }
        }
    }

    private static void ValidateReferences(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var inline in GetInlineDescendants(location.Node))
            {
                switch (inline)
                {
                    case Link link when link.Target.StartsWith("flow:", StringComparison.OrdinalIgnoreCase):
                        ValidateLink(document, location.Node.Id, link, diagnostics);
                        break;
                    case FootnoteReference reference:
                        ValidateFootnoteReference(document, location.Node.Id, reference, diagnostics);
                        break;
                }
            }
        }
    }

    private static void ValidateLink(
        FlowDocument document,
        NodeId sourceId,
        Link link,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (!DocumentAnchor.TryParse(link.Target, out var anchor))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidAnchor,
                $"Link in node '{sourceId}' contains invalid Flow anchor '{link.Target}'.",
                sourceId));
            return;
        }

        if (!document.TryResolveAnchor(anchor, out _))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.UnresolvedAnchor,
                $"Link in node '{sourceId}' targets unresolved anchor '{anchor}'.",
                sourceId,
                anchor));
        }
    }

    private static void ValidateFootnoteReference(
        FlowDocument document,
        NodeId sourceId,
        FootnoteReference reference,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (!document.Index.TryGetUniqueNode(reference.TargetId, out var target) || target is not Footnote)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.UnresolvedFootnoteReference,
                $"Footnote reference in node '{sourceId}' does not target one unique footnote '{reference.TargetId}'.",
                sourceId));
        }
    }

    private static void ValidateTableOfContents(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var tableOfContents in document.Index.Locations
                     .Select(static location => location.Node)
                     .OfType<TableOfContents>())
        {
            foreach (var entry in tableOfContents.Entries)
            {
                var resolves = document.TryResolveAnchor(entry.Target, out var target);
                var validTargetType = target is Chapter or Section or Heading;
                var validDepth = entry.Level <= tableOfContents.MaximumDepth;

                if (!resolves || !validTargetType || !validDepth)
                {
                    diagnostics.Add(Error(
                        ValidationDiagnosticCodes.InvalidTableOfContentsTarget,
                        $"Table of contents '{tableOfContents.Id}' has an invalid target '{entry.Target}' at level {entry.Level}.",
                        tableOfContents.Id,
                        entry.Target));
                }
            }
        }
    }

    private static IEnumerable<InlineNode> GetInlineDescendants(DocumentNode node)
    {
        IEnumerable<InlineNode> roots = node switch
        {
            Heading heading => heading.Content,
            Paragraph paragraph => paragraph.Content,
            Caption caption => caption.Content,
            TableOfContents tableOfContents => tableOfContents.Title.Concat(
                tableOfContents.Entries.SelectMany(static entry => entry.Label)),
            _ => [],
        };

        foreach (var root in roots)
        {
            yield return root;

            if (root is InlineContainerNode container)
            {
                foreach (var descendant in GetInlineDescendants(container.Children))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<InlineNode> GetInlineDescendants(IEnumerable<InlineNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;

            if (root is InlineContainerNode container)
            {
                foreach (var descendant in GetInlineDescendants(container.Children))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static ValidationDiagnostic Error(
        string code,
        string message,
        NodeId? nodeId = null,
        DocumentAnchor? anchor = null) =>
        new(code, ValidationSeverity.Error, message, nodeId, anchor);
}
