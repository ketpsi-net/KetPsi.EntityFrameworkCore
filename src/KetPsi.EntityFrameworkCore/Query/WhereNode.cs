using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>A <c>Where</c> predicate and the values captured when the operator ran.</summary>
internal sealed class WhereNode(QueryNode parent, PredicateTemplate predicate, object?[] values) : QueryNode(parent)
{
    public PredicateTemplate Predicate { get; } = predicate;

    /// <summary>The value slots of <see cref="Predicate"/>, indexed by the template's value indexes.</summary>
    public IReadOnlyList<object?> Values { get; } = values;
}
