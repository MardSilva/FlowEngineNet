using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Epub;

internal enum EpubImageFormat
{
    Jpeg,
    Png,
    Gif,
    WebP,
    Svg,
}

internal sealed record EpubImageInspection(
    EpubImageFormat Format,
    string MediaType,
    int? Width,
    int? Height,
    bool IsAnimated = false);

internal static class EpubImageInspector
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static bool TryInspect(
        ReadOnlySpan<byte> data,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EpubImageInspection? inspection,
        out string? failure)
    {
        inspection = null;
        failure = null;
        if (data.Length >= 24
            && data[..8].SequenceEqual(PngSignature)
            && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
            if (!TryDimensions(width, height, out var dimensions, out failure))
            {
                return false;
            }

            inspection = new EpubImageInspection(EpubImageFormat.Png, "image/png", dimensions.Width, dimensions.Height);
            return true;
        }

        if (data.Length >= 10
            && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
            if (!TryDimensions(width, height, out var dimensions, out failure)
                || !TryCountGifFrames(data, out var frames))
            {
                failure ??= "The GIF block structure is invalid.";
                return false;
            }

            inspection = new EpubImageInspection(
                EpubImageFormat.Gif,
                "image/gif",
                dimensions.Width,
                dimensions.Height,
                IsAnimated: frames > 1);
            return true;
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            if (!TryReadWebPDimensions(data, out var width, out var height))
            {
                failure = "The WebP dimensions could not be read from a supported VP8, VP8L, or VP8X header.";
                return false;
            }

            inspection = new EpubImageInspection(EpubImageFormat.WebP, "image/webp", width, height);
            return true;
        }

        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8)
        {
            if (!TryReadJpegDimensions(data, out var width, out var height))
            {
                failure = "The JPEG contains no readable start-of-frame dimensions.";
                return false;
            }

            inspection = new EpubImageInspection(EpubImageFormat.Jpeg, "image/jpeg", width, height);
            return true;
        }

        if (!LooksLikeXml(data))
        {
            failure = "The bytes do not match a supported JPEG, PNG, GIF, WebP, or SVG signature.";
            return false;
        }

        return TryInspectSvg(data, out inspection, out failure);
    }

    internal static bool IsSupportedDeclaredMediaType(string mediaType) =>
        NormalizeMediaType(mediaType) is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/svg+xml";

    internal static bool MediaTypeMatches(string declared, string detected)
    {
        var normalized = NormalizeMediaType(declared);
        return string.Equals(normalized, detected, StringComparison.Ordinal)
            || detected == "image/jpeg" && normalized is "image/jpg" or "image/pjpeg";
    }

    private static string NormalizeMediaType(string value) =>
        value.Split(';', 2)[0].Trim().ToLowerInvariant();

    private static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        var offset = data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        while (offset < data.Length && data[offset] is 9 or 10 or 13 or 32)
        {
            offset++;
        }

        return offset < data.Length && data[offset] == '<';
    }

    private static bool TryInspectSvg(
        ReadOnlySpan<byte> data,
        out EpubImageInspection? inspection,
        out string? failure)
    {
        inspection = null;
        failure = null;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = data.Length,
                MaxCharactersFromEntities = 0,
            };
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            XNamespace svgNamespace = "http://www.w3.org/2000/svg";
            if (document.Root?.Name != svgNamespace + "svg")
            {
                failure = "The bytes do not match a supported image signature or an SVG root element.";
                return false;
            }

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (element.Name.Namespace == svgNamespace
                    && element.Name.LocalName is "script" or "foreignObject" or "style")
                {
                    failure = $"SVG element <{element.Name.LocalName}> can contain active or externally loaded content.";
                    return false;
                }

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                        || attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.LocalName is "href" or "src"
                        && !attribute.Value.StartsWith('#'))
                    {
                        failure = $"SVG attribute '{attribute.Name}' can execute code or load an external resource.";
                        return false;
                    }
                }
            }

            var width = ReadSvgLength(document.Root.Attribute("width")?.Value);
            var height = ReadSvgLength(document.Root.Attribute("height")?.Value);
            if ((width is null || height is null)
                && TryReadViewBox(document.Root.Attribute("viewBox")?.Value, out var viewBoxWidth, out var viewBoxHeight))
            {
                width ??= viewBoxWidth;
                height ??= viewBoxHeight;
            }

            inspection = new EpubImageInspection(EpubImageFormat.Svg, "image/svg+xml", width, height);
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or DecoderFallbackException)
        {
            failure = $"The SVG XML is invalid or unsafe: {exception.Message}";
            return false;
        }
    }

    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 2;
        while (offset + 3 < data.Length)
        {
            while (offset < data.Length && data[offset] != 0xff)
            {
                offset++;
            }

            while (offset < data.Length && data[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                return false;
            }

            var marker = data[offset++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                return false;
            }

            if (IsJpegStartOfFrame(marker) && segmentLength >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7
            or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static bool TryCountGifFrames(ReadOnlySpan<byte> data, out int frames)
    {
        frames = 0;
        var packed = data[10];
        var offset = 13;
        if ((packed & 0x80) != 0)
        {
            offset += 3 * (1 << ((packed & 0x07) + 1));
        }

        while (offset < data.Length)
        {
            switch (data[offset++])
            {
                case 0x3b:
                    return frames > 0;
                case 0x21:
                    if (offset >= data.Length)
                    {
                        return false;
                    }

                    offset++;
                    if (!SkipGifSubBlocks(data, ref offset))
                    {
                        return false;
                    }

                    break;
                case 0x2c:
                    frames++;
                    if (offset + 9 > data.Length)
                    {
                        return false;
                    }

                    var imagePacked = data[offset + 8];
                    offset += 9;
                    if ((imagePacked & 0x80) != 0)
                    {
                        offset += 3 * (1 << ((imagePacked & 0x07) + 1));
                    }

                    if (offset >= data.Length)
                    {
                        return false;
                    }

                    offset++;
                    if (!SkipGifSubBlocks(data, ref offset))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool SkipGifSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (length == 0)
            {
                return true;
            }

            if (offset + length > data.Length)
            {
                return false;
            }

            offset += length;
        }

        return false;
    }

    private static bool TryReadWebPDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30)
        {
            return false;
        }

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadUInt24LittleEndian(data.Slice(24, 3));
            height = 1 + ReadUInt24LittleEndian(data.Slice(27, 3));
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8) && data[20] == 0x2f && data.Length >= 25)
        {
            width = 1 + data[21] + ((data[22] & 0x3f) << 8);
            height = 1 + (data[22] >> 6) + (data[23] << 2) + ((data[24] & 0x0f) << 10);
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8) && data.Length >= 30
            && data.Slice(23, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3fff;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3fff;
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> value) =>
        value[0] | value[1] << 8 | value[2] << 16;

    private static bool TryDimensions(
        uint width,
        uint height,
        out (int Width, int Height) dimensions,
        out string? failure)
    {
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            dimensions = default;
            failure = "The image dimensions are zero or exceed the supported integer range.";
            return false;
        }

        dimensions = ((int)width, (int)height);
        failure = null;
        return true;
    }

    private static int? ReadSvgLength(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            && result > 0
            && result <= int.MaxValue
                ? (int)Math.Ceiling(result)
                : null;
    }

    private static bool TryReadViewBox(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = value?.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts is { Length: 4 }
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth)
            && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeight)
            && parsedWidth > 0
            && parsedHeight > 0
            && parsedWidth <= int.MaxValue
            && parsedHeight <= int.MaxValue
            && (width = (int)Math.Ceiling(parsedWidth)) > 0
            && (height = (int)Math.Ceiling(parsedHeight)) > 0;
    }
}
