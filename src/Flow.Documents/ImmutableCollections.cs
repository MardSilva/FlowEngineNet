using System.Collections.Immutable;

namespace Flow.Documents;

internal static class ImmutableCollections
{
    internal static ImmutableArray<T> CopyOf<T>(IEnumerable<T> source, string parameterName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);

        var items = source.ToImmutableArray();
        if (items.Any(static item => item is null))
        {
            throw new ArgumentException("Collections cannot contain null items.", parameterName);
        }

        return items;
    }
}
