using System.Text.RegularExpressions;

namespace Flow.Cli;

internal sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease, string value)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Value = value;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public string Value { get; }

    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value[0] is 'v' or 'V' ? value[1..] : value;
        var match = VersionPattern().Match(candidate);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !int.TryParse(match.Groups["minor"].Value, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["prerelease"].Success
            ? match.Groups["prerelease"].Value
            : null;
        version = new SemanticVersion(major, minor, patch, prerelease, candidate);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numeric = Major.CompareTo(other.Major);
        if (numeric == 0)
        {
            numeric = Minor.CompareTo(other.Minor);
        }

        if (numeric == 0)
        {
            numeric = Patch.CompareTo(other.Patch);
        }

        if (numeric != 0)
        {
            return numeric;
        }

        if (Prerelease is null || other.Prerelease is null)
        {
            return Prerelease is null
                ? other.Prerelease is null ? 0 : 1
                : -1;
        }

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = CompareIdentifier(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() => Value;

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = long.TryParse(left, out var leftValue);
        var rightNumeric = long.TryParse(right, out var rightValue);
        if (leftNumeric && rightNumeric)
        {
            return leftValue.CompareTo(rightValue);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    [GeneratedRegex(
        "^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<prerelease>(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex VersionPattern();
}
