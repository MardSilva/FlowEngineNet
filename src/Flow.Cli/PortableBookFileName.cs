using System.Globalization;
using System.Text;

namespace Flow.Cli;

internal static class PortableBookFileName
{
    private const int MaximumStemLength = 96;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    internal static string FromTitle(string title, string sourcePath)
    {
        var stem = Slug(title);
        if (stem.Length == 0)
        {
            stem = Slug(Path.GetFileNameWithoutExtension(sourcePath));
        }

        if (stem.Length == 0)
        {
            stem = "imported_book";
        }

        if (WindowsReservedNames.Contains(stem))
        {
            stem = $"book_{stem}";
        }

        return $"{stem}.flow.json";
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaximumStemLength));
        var separatorPending = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separatorPending && builder.Length > 0 && builder.Length < MaximumStemLength)
                {
                    builder.Append('_');
                }

                if (builder.Length < MaximumStemLength)
                {
                    builder.Append(lower);
                }

                separatorPending = false;
            }
            else if (builder.Length > 0)
            {
                separatorPending = true;
            }

            if (builder.Length >= MaximumStemLength)
            {
                break;
            }
        }

        return builder.ToString().TrimEnd('_');
    }
}
