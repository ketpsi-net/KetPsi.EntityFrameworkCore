namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Select</c>: the statement reads only the entity properties the generated projection reader uses. Operators
/// after it work on the projected rows.
/// </summary>
internal sealed class ProjectionNode(QueryNode parent, IReadOnlyList<string> propertyNames) : QueryNode(parent)
{
    /// <summary>The entity properties the reader reads, in the order of its ordinals. A name can repeat.</summary>
    public IReadOnlyList<string> PropertyNames { get; } = propertyNames;

    /// <summary>The columns the statement selects: <see cref="PropertyNames"/> once each, in first-seen order.</summary>
    public IReadOnlyList<string> Columns { get; } = [.. propertyNames.Distinct(StringComparer.Ordinal)];
}
