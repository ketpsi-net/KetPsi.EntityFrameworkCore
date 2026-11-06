using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Where</c> on groups, before or after the grouped <c>Select</c>: the predicate filters the groups (<c>HAVING</c>).
/// </summary>
/// <param name="parent">The previous node.</param>
/// <param name="predicate">
/// The predicate over the group's values: each property name is the index of an operand, such as <c>"0"</c> for
/// <c>operands[0]</c>.
/// </param>
/// <param name="operands">The values the predicate compares.</param>
/// <param name="values">The value slots of the predicate and of the operands' count predicates.</param>
internal sealed class HavingNode(QueryNode parent, PredicateTemplate predicate, IReadOnlyList<GroupValue> operands, IReadOnlyList<object?> values)
    : QueryNode(parent)
{
    public PredicateTemplate Predicate { get; } = predicate;

    public IReadOnlyList<GroupValue> Operands { get; } = operands;

    public IReadOnlyList<object?> Values { get; } = values;
}
