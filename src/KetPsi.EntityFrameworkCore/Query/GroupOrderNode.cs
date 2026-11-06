using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary><c>OrderBy</c>, <c>ThenBy</c> or a descending one on a value of the groups, before or after the grouped <c>Select</c>.</summary>
/// <param name="parent">The previous node.</param>
/// <param name="value">The key part or aggregate the groups are ordered by.</param>
/// <param name="values">The value slots of the value's count predicate.</param>
/// <param name="descending">Whether the order is descending.</param>
/// <param name="isThenBy">Whether it continues the ordering before it.</param>
internal sealed class GroupOrderNode(QueryNode parent, GroupValue value, IReadOnlyList<object?> values, bool descending, bool isThenBy)
    : QueryNode(parent)
{
    public GroupValue Value { get; } = value;

    public IReadOnlyList<object?> Values { get; } = values;

    public bool Descending { get; } = descending;

    public bool IsThenBy { get; } = isThenBy;
}
