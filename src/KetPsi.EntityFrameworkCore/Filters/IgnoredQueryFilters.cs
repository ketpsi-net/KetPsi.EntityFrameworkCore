using KetPsi.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Filters;

/// <summary>
/// The query filters a chain disables. Like EF Core, <c>IgnoreQueryFilters</c> applies to the whole query
/// wherever it appears in the chain.
/// </summary>
internal sealed class IgnoredQueryFilters
{
    private readonly HashSet<string> _keys;

    private IgnoredQueryFilters(bool all, HashSet<string> keys)
    {
        All = all;
        _keys = keys;
    }

    public static IgnoredQueryFilters None { get; } = new(all: false, []);

    /// <summary>Whether every filter is disabled.</summary>
    public bool All { get; }

    public static IgnoredQueryFilters FromChain(QueryNode tail)
    {
        ArgumentNullException.ThrowIfNull(tail);

        var all = false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var node = tail; node is not null; node = node.Parent)
        {
            if (node is not IgnoreQueryFiltersNode ignore)
            {
                continue;
            }

            if (ignore.FilterKeys is null)
            {
                all = true;
            }
            else
            {
                keys.UnionWith(ignore.FilterKeys);
            }
        }

        return all || keys.Count > 0 ? new IgnoredQueryFilters(all, keys) : None;
    }

    /// <summary>Whether the filter with <paramref name="filterKey"/> is disabled. An unnamed filter is only disabled by <see cref="All"/>.</summary>
    public bool IsIgnored(string? filterKey) => All || (filterKey is not null && _keys.Contains(filterKey));
}
