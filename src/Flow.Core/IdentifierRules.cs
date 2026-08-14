namespace Flow.Core;

internal static class IdentifierRules
{
    internal static string ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > 128)
        {
            throw new ArgumentException("A stable identifier cannot exceed 128 characters.", parameterName);
        }

        if (value[0] is < 'a' or > 'z')
        {
            throw new ArgumentException("A stable identifier must start with a lowercase ASCII letter.", parameterName);
        }

        foreach (var character in value)
        {
            var isAllowed = character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_'
                or '.';

            if (!isAllowed)
            {
                throw new ArgumentException(
                    "A stable identifier can contain only lowercase ASCII letters, digits, hyphens, underscores, and periods.",
                    parameterName);
            }
        }

        return value;
    }
}
