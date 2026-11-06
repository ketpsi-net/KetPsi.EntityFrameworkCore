using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Select</c> on groups: the statement selects one column per value, in order, and the generated reader builds each
/// result from them. Operators after it work on the projected groups.
/// </summary>
/// <param name="parent">The previous node.</param>
/// <param name="values">The values the reader reads; its ordinal for value <c>i</c> is <c>i</c>.</param>
/// <param name="predicateValues">The value slots of the values' count predicates.</param>
internal sealed class GroupSelectNode(QueryNode parent, IReadOnlyList<GroupValue> values, IReadOnlyList<object?> predicateValues)
    : QueryNode(parent)
{
    public IReadOnlyList<GroupValue> Values { get; } = values;

    public IReadOnlyList<object?> PredicateValues { get; } = predicateValues;
}
