using System.Collections.Immutable;
using System.Xml.Linq;
using Flow.Rendering.Html;

namespace Flow.Epub.Corpus;

internal static class HtmlBookPackageVerifier
{
    internal static HtmlBookPackageEvidence Verify(
        HtmlBookPackage package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var files = package.Files.Select(static file => file.Path).ToHashSet(StringComparer.Ordinal);
        var htmlFiles = package.Files
            .Where(static file => file.MediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var idsByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var elementNames = new HashSet<string>(StringComparer.Ordinal);
        var externalReferences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in htmlFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = ParseHtml(file);
            idsByPath[file.Path] = document.Descendants()
                .Select(static element => (string?)element.Attribute("id"))
                .Where(static id => !string.IsNullOrEmpty(id))
                .Select(static id => id!)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in document.Descendants().Select(static element => element.Name.LocalName))
            {
                elementNames.Add(name);
            }
        }

        foreach (var file in htmlFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = file.Path;
            var document = ParseHtml(file);
            foreach (var attribute in document.Descendants().Attributes()
                         .Where(static attribute => attribute.Name.LocalName is "href" or "src"))
            {
                var target = attribute.Value;
                if (string.IsNullOrEmpty(target)
                    || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsSafeExternalReference(target))
                {
                    externalReferences[target] = externalReferences.GetValueOrDefault(target) + 1;
                    continue;
                }

                var hashIndex = target.IndexOf('#');
                var pathPart = hashIndex < 0 ? target : target[..hashIndex];
                var fragment = hashIndex < 0 ? null : Uri.UnescapeDataString(target[(hashIndex + 1)..]);
                var resolvedPath = string.IsNullOrEmpty(pathPart)
                    ? currentPath
                    : ResolvePackagePath(currentPath, Uri.UnescapeDataString(pathPart));
                if (!files.Contains(resolvedPath))
                {
                    throw new InvalidDataException("The HTML package contains an unresolved local file reference.");
                }

                if (!string.IsNullOrEmpty(fragment)
                    && idsByPath.TryGetValue(resolvedPath, out var targetIds)
                    && !targetIds.Contains(fragment))
                {
                    throw new InvalidDataException("The HTML package contains an unresolved local fragment reference.");
                }
            }
        }

        return new HtmlBookPackageEvidence(
            package.Files.Length,
            package.Files.Sum(static file => (long)file.Content.Length),
            elementNames,
            idsByPath
                .SelectMany(static pair => pair.Value.Select(id => (Id: id, Path: pair.Key)))
                .GroupBy(static item => item.Id, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    static group => group.Key,
                    static group => group.Select(static item => item.Path)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray(),
                    StringComparer.Ordinal),
            externalReferences.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private static bool IsSafeExternalReference(string target)
    {
        if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            if (target.Length == "mailto:".Length
                || target.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
            {
                return false;
            }

            try
            {
                return !Uri.UnescapeDataString(target).Any(char.IsControl);
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        return Uri.TryCreate(target, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https";
    }

    private static XDocument ParseHtml(HtmlBookFile file) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(file.Content.AsSpan()));

    private static string ResolvePackagePath(string currentPath, string relativePath)
    {
        var segments = currentPath.Split('/').SkipLast(1).ToList();
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidDataException("The HTML package reference escapes its root.");
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }
}

internal sealed record HtmlBookPackageEvidence(
    int FileCount,
    long Bytes,
    IReadOnlySet<string> ElementNames,
    ImmutableDictionary<string, ImmutableArray<string>> PathsById,
    ImmutableDictionary<string, int> ExternalReferences);
