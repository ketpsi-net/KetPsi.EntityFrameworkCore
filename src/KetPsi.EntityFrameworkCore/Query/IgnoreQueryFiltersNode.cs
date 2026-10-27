namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>Disables model query filters: all of them, or only the named ones.</summary>
internal sealed class IgnoreQueryFiltersNode(QueryNode parent, IReadOnlyList<string>? filterKeys) : QueryNode(parent)
{
    /// <summary>The filter keys to ignore, or <see langword="null"/> to ignore every filter.</summary>
    public IReadOnlyList<string>? FilterKeys { get; } = filterKeys;
}
