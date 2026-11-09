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

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var all = Collect(tail, keys);
        return all || keys.Count > 0 ? new IgnoredQueryFilters(all, keys) : None;
    }

    // The inner query of a Join is part of the query too.
    private static bool Collect(QueryNode tail, HashSet<string> keys)
    {
        var all = false;
        for (var node = tail; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case IgnoreQueryFiltersNode { FilterKeys: null }:
                    all = true;
                    break;
                case IgnoreQueryFiltersNode ignore:
                    keys.UnionWith(ignore.FilterKeys);
                    break;
                case JoinNode join:
                    all |= Collect(join.Inner, keys);
                    break;
            }
        }

        return all;
    }

    /// <summary>Whether the filter with <paramref name="filterKey"/> is disabled. An unnamed filter is only disabled by <see cref="All"/>.</summary>
    public bool IsIgnored(string? filterKey) => All || (filterKey is not null && _keys.Contains(filterKey));
}
