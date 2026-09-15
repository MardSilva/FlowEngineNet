using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Flow.Documents;

namespace Flow.Epub;

internal sealed class EpubCssProcessor
{
    private static readonly HashSet<string> SupportedProperties = new(StringComparer.Ordinal)
    {
        "font-family", "font-size", "font-weight", "font-style", "line-height", "text-align",
        "text-transform", "text-decoration", "letter-spacing", "margin", "margin-top", "margin-bottom",
        "margin-block-start", "margin-block-end", "text-indent",
    };

    private static readonly HashSet<string> InheritedProperties = new(StringComparer.Ordinal)
    {
        "font-family", "font-size", "font-weight", "font-style", "line-height", "text-align",
        "text-transform", "text-decoration", "letter-spacing", "text-indent",
    };

    private readonly IReadOnlyDictionary<string, ZipArchiveEntry> entries;
    private readonly IReadOnlyDictionary<string, string> manifestMediaTypes;
    private readonly EpubImportLimits limits;
    private readonly List<EpubDiagnostic> diagnostics;
    private readonly HashSet<string> consumedResources;
    private readonly Dictionary<CssDiagnosticKey, int> aggregatedDiagnostics = [];
    private int order;

    internal EpubCssProcessor(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyDictionary<string, string> manifestMediaTypes,
        EpubImportLimits limits,
        List<EpubDiagnostic> diagnostics,
        HashSet<string> consumedResources)
    {
        this.entries = entries;
        this.manifestMediaTypes = manifestMediaTypes;
        this.limits = limits;
        this.diagnostics = diagnostics;
        this.consumedResources = consumedResources;
    }

    internal async Task<IReadOnlyDictionary<XElement, TypographyStyle>> ProcessAsync(
        IEnumerable<(string ResourcePath, XDocument Document)> documents,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<XElement, TypographyStyle>();
        foreach (var (resourcePath, document) in documents)
        {
            var rules = await ReadRulesAsync(resourcePath, document, cancellationToken).ConfigureAwait(false);
            var body = document.Root?.Element(EpubNamespaces.Xhtml + "body");
            if (body is null)
            {
                continue;
            }

            var computed = new Dictionary<XElement, IReadOnlyDictionary<string, string>>();
            foreach (var element in body.DescendantsAndSelf())
            {
                var values = new Dictionary<string, CascadedValue>(StringComparer.Ordinal);
                var parent = element.Parent;
                if (parent is not null && computed.TryGetValue(parent, out var inherited))
                {
                    foreach (var pair in inherited.Where(static pair => InheritedProperties.Contains(pair.Key)))
                    {
                        values[pair.Key] = new CascadedValue(pair.Value, -1, -1);
                    }
                }

                foreach (var rule in rules)
                {
                    if (!rule.Selector.Matches(element))
                    {
                        continue;
                    }

                    ApplyDeclarations(values, rule.Declarations, rule.Selector.Specificity, rule.Order);
                }

                if ((string?)element.Attribute("style") is { } inlineStyle)
                {
                    ApplyDeclarations(
                        values,
                        ParseDeclarations(inlineStyle, resourcePath, "style attribute"),
                        specificity: 1_000,
                        ++order);
                }

                var plain = values.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.Ordinal);
                computed[element] = plain;
                if (values.Values.Any(static value => value.Specificity >= 0)
                    && IsInlineOnlyElement(element))
                {
                    Report(
                        EpubDiagnosticCodes.CssTargetNotRepresentable,
                        resourcePath,
                        element.Name.LocalName,
                        $"CSS matched inline <{element.Name.LocalName}> content, but the current Flow inline model has no independently addressable presentation node");
                }

                if (CreateTypographyStyle(element, plain, resourcePath) is { IsEmpty: false } style)
                {
                    result[element] = style;
                }
            }
        }

        FlushDiagnostics();
        return result;
    }

    private async Task<IReadOnlyList<CssRule>> ReadRulesAsync(
        string resourcePath,
        XDocument document,
        CancellationToken cancellationToken)
    {
        var rules = new List<CssRule>();
        foreach (var element in document.Descendants().Where(static element =>
                     element.Name == EpubNamespaces.Xhtml + "style"
                     || element.Name == EpubNamespaces.Xhtml + "link"))
        {
            if (element.Name.LocalName == "style")
            {
                var type = (string?)element.Attribute("type");
                if (type is not null && !type.Equals("text/css", StringComparison.OrdinalIgnoreCase))
                {
                    Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, "style-type", $"Style element type '{type}' is unsupported");
                    continue;
                }

                rules.AddRange(ParseStylesheet(element.Value, resourcePath));
                continue;
            }

            var rel = ((string?)element.Attribute("rel") ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!rel.Contains("stylesheet", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = (string?)element.Attribute("href");
            if (string.IsNullOrWhiteSpace(href)
                || Uri.TryCreate(href, UriKind.Absolute, out _)
                || !EpubArchiveUtilities.TryNormalizeArchivePath(
                    EpubArchiveUtilities.GetDirectory(resourcePath),
                    href,
                    out var stylesheetPath))
            {
                Report(EpubDiagnosticCodes.ExternalStylesheetBlocked, resourcePath, "external", $"Stylesheet reference '{href ?? "(missing)"}' is external or unsafe and was not loaded");
                continue;
            }

            var media = (string?)element.Attribute("media");
            if (!string.IsNullOrWhiteSpace(media) && !media.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, "media", $"Stylesheet media condition '{media}' cannot be evaluated during semantic import");
                continue;
            }

            if (!manifestMediaTypes.TryGetValue(stylesheetPath, out var manifestType))
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, stylesheetPath, "manifest", "Stylesheet is absent from the OPF manifest");
                continue;
            }

            if (!manifestType.Equals("text/css", StringComparison.OrdinalIgnoreCase))
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, stylesheetPath, "mime", $"Stylesheet manifest MIME '{manifestType}' is not text/css");
                continue;
            }

            if (!entries.TryGetValue(stylesheetPath, out var entry))
            {
                Report(EpubDiagnosticCodes.MissingResource, stylesheetPath, "missing", "Stylesheet resource is missing from the archive");
                continue;
            }

            if (entry.Length > limits.MaximumStylesheetBytes)
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, stylesheetPath, "bytes", $"Stylesheet exceeds the configured {limits.MaximumStylesheetBytes}-byte limit");
                continue;
            }

            try
            {
                await using var stream = entry.Open();
                await using var buffer = await EpubArchiveUtilities.CopyWithLimitAsync(
                    stream,
                    limits.MaximumStylesheetBytes,
                    cancellationToken).ConfigureAwait(false);
                var css = new UTF8Encoding(false, true).GetString(buffer.ToArray());
                if (css.StartsWith('\uFEFF'))
                {
                    css = css[1..];
                }
                consumedResources.Add(stylesheetPath);
                rules.AddRange(ParseStylesheet(css, stylesheetPath));
            }
            catch (Exception exception) when (exception is DecoderFallbackException or EpubLimitExceededException)
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, stylesheetPath, "encoding", $"Stylesheet is invalid or exceeds limits: {exception.Message}");
            }
        }

        return rules;
    }

    private IEnumerable<CssRule> ParseStylesheet(string css, string resourcePath)
    {
        css = RemoveComments(css, resourcePath);
        var position = 0;
        while (position < css.Length)
        {
            SkipWhitespace(css, ref position);
            if (position >= css.Length)
            {
                yield break;
            }

            var opening = FindOutsideQuotes(css, '{', position);
            if (opening < 0 || !TryFindClosingBrace(css, opening, out var closing))
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, resourcePath, "syntax", "A CSS rule has unbalanced braces");
                yield break;
            }

            var selectorText = css[position..opening].Trim();
            var body = css[(opening + 1)..closing];
            position = closing + 1;
            if (selectorText.StartsWith('@'))
            {
                Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, selectorText.Split((char[]?)null, 2)[0], $"CSS at-rule '{selectorText}' is outside the safe subset");
                continue;
            }

            var declarations = ParseDeclarations(body, resourcePath, selectorText);
            foreach (var selectorPart in SplitOutsideQuotes(selectorText, ','))
            {
                if (!CssSelector.TryParse(selectorPart.Trim(), out var selector))
                {
                    Report(EpubDiagnosticCodes.UnsupportedCssSelector, resourcePath, selectorPart.Trim(), $"CSS selector '{selectorPart.Trim()}' is outside the element/class/ID subset");
                    continue;
                }

                yield return new CssRule(selector, declarations, ++order);
            }
        }
    }

    private IReadOnlyDictionary<string, string> ParseDeclarations(
        string body,
        string resourcePath,
        string source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in SplitOutsideQuotes(body, ';'))
        {
            if (string.IsNullOrWhiteSpace(declaration))
            {
                continue;
            }

            var separator = FindOutsideQuotes(declaration, ':', 0);
            if (separator <= 0)
            {
                Report(EpubDiagnosticCodes.InvalidStylesheet, resourcePath, source, $"Malformed declaration '{declaration.Trim()}' was ignored");
                continue;
            }

            var property = declaration[..separator].Trim().ToLowerInvariant();
            var value = declaration[(separator + 1)..].Trim();
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, property, $"CSS !important on '{property}' is outside the supported cascade");
                continue;
            }

            if (!SupportedProperties.Contains(property))
            {
                Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, property, $"CSS property '{property}' is outside the typed Flow subset");
                continue;
            }

            if (value.Contains("url(", StringComparison.OrdinalIgnoreCase)
                || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                Report(EpubDiagnosticCodes.ExternalStylesheetBlocked, resourcePath, property, $"CSS value for '{property}' contains a URL and was blocked");
                continue;
            }

            if (property == "margin")
            {
                ExpandMargin(value, result, resourcePath);
            }
            else
            {
                result[property] = value;
            }
        }

        return result;
    }

    private void ExpandMargin(
        string value,
        IDictionary<string, string> declarations,
        string resourcePath)
    {
        var values = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length is < 1 or > 4)
        {
            Report(EpubDiagnosticCodes.InvalidCssValue, resourcePath, "margin", $"CSS margin value '{value}' is invalid");
            return;
        }

        declarations["margin-block-start"] = values[0];
        declarations["margin-block-end"] = values.Length switch
        {
            1 => values[0],
            2 => values[0],
            _ => values[2],
        };
    }

    private static void ApplyDeclarations(
        IDictionary<string, CascadedValue> target,
        IReadOnlyDictionary<string, string> declarations,
        int specificity,
        int order)
    {
        foreach (var pair in declarations)
        {
            if (!target.TryGetValue(pair.Key, out var current)
                || specificity > current.Specificity
                || specificity == current.Specificity && order >= current.Order)
            {
                target[pair.Key] = new CascadedValue(pair.Value, specificity, order);
            }
        }
    }

    private TypographyStyle? CreateTypographyStyle(
        XElement element,
        IReadOnlyDictionary<string, string> values,
        string resourcePath)
    {
        string? fontFamily = null;
        Length? fontSize = null;
        FontWeight? fontWeight = null;
        FontStyle? fontStyle = null;
        double? lineHeight = null;
        Length? letterSpacing = null;
        TextAlignment? alignment = null;
        TextTransform? transform = null;
        TextDecoration? decoration = null;
        Length? marginBefore = null;
        Length? marginAfter = null;
        Length? indent = null;

        foreach (var pair in values)
        {
            var valid = pair.Key switch
            {
                "font-family" => TryFontFamily(pair.Value, out fontFamily),
                "font-size" => TryLength(pair.Value, positive: true, out fontSize),
                "font-weight" => TryFontWeight(pair.Value, out fontWeight),
                "font-style" => TryFontStyle(pair.Value, out fontStyle),
                "line-height" => TryLineHeight(pair.Value, out lineHeight),
                "letter-spacing" => TryNormalLength(pair.Value, out letterSpacing),
                "text-align" => TryTextAlignment(pair.Value, out alignment),
                "text-transform" => EnumValue(pair.Value, out transform),
                "text-decoration" => TryTextDecoration(pair.Value, out decoration),
                "text-indent" => TryLength(pair.Value, positive: false, out indent),
                "margin-top" or "margin-block-start" when element.Name.LocalName == "p" =>
                    TryLength(pair.Value, positive: false, out marginBefore),
                "margin-bottom" or "margin-block-end" when element.Name.LocalName == "p" =>
                    TryLength(pair.Value, positive: false, out marginAfter),
                "margin-top" or "margin-bottom" or "margin-block-start" or "margin-block-end" =>
                    ReportUnsupportedTarget(resourcePath, pair.Key, element),
                _ => false,
            };

            if (!valid && pair.Key is not ("margin-top" or "margin-bottom" or "margin-block-start" or "margin-block-end"))
            {
                Report(EpubDiagnosticCodes.InvalidCssValue, resourcePath, pair.Key, $"CSS value '{pair.Value}' for '{pair.Key}' cannot be represented by Flow typed presentation");
            }
        }

        var style = new TypographyStyle(
            fontFamily,
            fontSize,
            fontWeight,
            fontStyle,
            lineHeight,
            letterSpacing,
            alignment,
            transform,
            marginBefore,
            marginAfter,
            indent,
            decoration);
        return style.IsEmpty ? null : style;
    }

    private bool ReportUnsupportedTarget(string resourcePath, string property, XElement element)
    {
        Report(EpubDiagnosticCodes.UnsupportedCssProperty, resourcePath, $"{property}:{element.Name.LocalName}", $"CSS property '{property}' is only mapped as semantic paragraph spacing and was ignored on <{element.Name.LocalName}>");
        return false;
    }

    private static bool TryFontFamily(string value, out string? result)
    {
        result = SplitOutsideQuotes(value, ',').FirstOrDefault()?.Trim().Trim('"', '\'');
        return !string.IsNullOrWhiteSpace(result)
            && result.IndexOfAny([',', ';', '{', '}', '(', ')', ':', '\\']) < 0
            && !result.Any(char.IsControl);
    }

    private static bool TryLength(string value, bool positive, out Length? result)
    {
        result = null;
        value = value.Trim().ToLowerInvariant();
        if (!TrySplitNumberAndUnit(value, out var number, out var unit)
            || positive && number <= 0)
        {
            return false;
        }

        result = unit switch
        {
            "px" => Length.Px(number),
            "em" => Length.Em(number),
            "rem" => Length.Rem(number),
            "%" => Length.Percent(number),
            "" when number == 0 => Length.Px(0),
            _ => null,
        };
        return result is not null;
    }

    private static bool TryNormalLength(string value, out Length? result)
    {
        if (value.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            result = Length.Em(0);
            return true;
        }

        return TryLength(value, positive: false, out result);
    }

    private static bool TrySplitNumberAndUnit(string value, out double number, out string unit)
    {
        number = 0;
        unit = new[] { "rem", "px", "em", "%" }
            .FirstOrDefault(candidate => value.EndsWith(candidate, StringComparison.Ordinal)) ?? string.Empty;
        var numberText = unit.Length == 0 ? value : value[..^unit.Length];
        if (unit.Length == 0 && value != "0" && value != "+0" && value != "-0" && value != "0.0")
        {
            return false;
        }

        return numberText.Length > 0
            && double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number);
    }

    private static bool TryFontWeight(string value, out FontWeight? result)
    {
        result = value.Trim().ToLowerInvariant() switch
        {
            "normal" or "400" => FontWeight.Normal,
            "bold" or "700" => FontWeight.Bold,
            "100" => FontWeight.Thin,
            "200" => FontWeight.ExtraLight,
            "300" => FontWeight.Light,
            "500" => FontWeight.Medium,
            "600" => FontWeight.SemiBold,
            "800" => FontWeight.ExtraBold,
            "900" => FontWeight.Black,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryFontStyle(string value, out FontStyle? result) => EnumValue(value, out result);

    private static bool TryLineHeight(string value, out double? result)
    {
        result = null;
        value = value.Trim();
        if (value.EndsWith('%')
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            && percent > 0)
        {
            result = percent / 100;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            && number > 0)
        {
            result = number;
            return true;
        }

        return false;
    }

    private static bool TryTextAlignment(string value, out TextAlignment? result)
    {
        result = value.Trim().ToLowerInvariant() switch
        {
            "left" or "start" => TextAlignment.Start,
            "right" or "end" => TextAlignment.End,
            "center" => TextAlignment.Center,
            "justify" => TextAlignment.Justify,
            _ => null,
        };
        return result is not null;
    }

    private static bool TryTextDecoration(string value, out TextDecoration? result)
    {
        result = TextDecoration.None;
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "none":
                    result = TextDecoration.None;
                    break;
                case "underline":
                    result |= TextDecoration.Underline;
                    break;
                case "line-through":
                    result |= TextDecoration.LineThrough;
                    break;
                default:
                    result = null;
                    return false;
            }
        }

        return true;
    }

    private static bool IsInlineOnlyElement(XElement element) => element.Name.LocalName is
        "span" or "strong" or "b" or "em" or "i" or "u" or "s" or "strike" or "del"
        or "code" or "a" or "small" or "abbr" or "cite" or "q" or "sub" or "sup" or "mark" or "time";

    private static bool EnumValue<TEnum>(string value, out TEnum? result)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private void Report(string code, string resource, string subject, string message)
    {
        var key = new CssDiagnosticKey(code, resource, subject, message);
        aggregatedDiagnostics[key] = aggregatedDiagnostics.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private void FlushDiagnostics()
    {
        foreach (var (key, count) in aggregatedDiagnostics)
        {
            var suffix = count == 1 ? string.Empty : $" ({count} occurrences)";
            diagnostics.Add(new EpubDiagnostic(key.Code, EpubDiagnosticSeverity.Warning, key.Message + suffix + ".", key.Resource));
        }
    }

    private string RemoveComments(string css, string resourcePath)
    {
        var result = new StringBuilder(css.Length);
        for (var index = 0; index < css.Length;)
        {
            if (index + 1 < css.Length && css[index] == '/' && css[index + 1] == '*')
            {
                var end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    Report(EpubDiagnosticCodes.InvalidStylesheet, resourcePath, "comment", "An unterminated CSS comment was ignored");
                    break;
                }

                index = end + 2;
                continue;
            }

            result.Append(css[index++]);
        }

        return result.ToString();
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position]))
        {
            position++;
        }
    }

    private static int FindOutsideQuotes(string value, char target, int start)
    {
        char quote = '\0';
        var parentheses = 0;
        for (var index = start; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || value[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '(')
            {
                parentheses++;
            }
            else if (character == ')')
            {
                parentheses--;
            }
            else if (character == target && parentheses == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitOutsideQuotes(string value, char separator)
    {
        var start = 0;
        while (start <= value.Length)
        {
            var index = FindOutsideQuotes(value, separator, start);
            if (index < 0)
            {
                yield return value[start..];
                yield break;
            }

            yield return value[start..index];
            start = index + 1;
        }
    }

    private static bool TryFindClosingBrace(string value, int opening, out int closing)
    {
        var depth = 1;
        char quote = '\0';
        for (var index = opening + 1; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote && value[index - 1] != '\\')
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                closing = index;
                return true;
            }
        }

        closing = -1;
        return false;
    }

    private sealed record CssRule(CssSelector Selector, IReadOnlyDictionary<string, string> Declarations, int Order);

    private sealed record CssSelector(string? Element, string? Id, IReadOnlyList<string> Classes, int Specificity)
    {
        internal bool Matches(XElement element) =>
            element.Name.Namespace == EpubNamespaces.Xhtml
            && (Element is null || element.Name.LocalName.Equals(Element, StringComparison.OrdinalIgnoreCase))
            && (Id is null || string.Equals((string?)element.Attribute("id"), Id, StringComparison.Ordinal))
            && Classes.All(required => ((string?)element.Attribute("class") ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains(required, StringComparer.Ordinal));

        internal static bool TryParse(string value, out CssSelector selector)
        {
            selector = null!;
            if (string.IsNullOrWhiteSpace(value)
                || value.Any(char.IsWhiteSpace)
                || value.IndexOfAny(['>', '+', '~', '[', ']', ':']) >= 0)
            {
                return false;
            }

            var position = 0;
            string? element = null;
            if (value[position] == '*')
            {
                position++;
            }
            else if (char.IsAsciiLetter(value[position]))
            {
                var start = position++;
                while (position < value.Length && IsNameCharacter(value[position]))
                {
                    position++;
                }

                element = value[start..position].ToLowerInvariant();
            }

            string? id = null;
            var classes = new List<string>();
            while (position < value.Length)
            {
                var marker = value[position++];
                if (marker is not ('.' or '#'))
                {
                    return false;
                }

                var start = position;
                while (position < value.Length && IsNameCharacter(value[position]))
                {
                    position++;
                }

                if (start == position)
                {
                    return false;
                }

                var name = value[start..position];
                if (marker == '#')
                {
                    if (id is not null)
                    {
                        return false;
                    }

                    id = name;
                }
                else
                {
                    classes.Add(name);
                }
            }

            if (element is null && id is null && classes.Count == 0 && value != "*")
            {
                return false;
            }

            selector = new CssSelector(element, id, classes, (id is null ? 0 : 100) + classes.Count * 10 + (element is null ? 0 : 1));
            return true;
        }

        private static bool IsNameCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is '-' or '_';
    }

    private readonly record struct CascadedValue(string Value, int Specificity, int Order);

    private readonly record struct CssDiagnosticKey(string Code, string Resource, string Subject, string Message);
}

internal static class EpubNamespaces
{
    internal static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
}
