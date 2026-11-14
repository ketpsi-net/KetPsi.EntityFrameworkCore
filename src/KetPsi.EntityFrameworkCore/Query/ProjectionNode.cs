using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Select</c>: the statement reads only the columns and entities the generated projection reader uses. Operators
/// after it work on the projected rows.
/// </summary>
/// <remarks>
/// A path starts at the queried entity and follows reference navigations: <c>"Name"</c> is <c>x.Name</c>,
/// <c>"Blog.Owner.Name"</c> is <c>x.Blog.Owner.Name</c>, and the entity path <c>""</c> is <c>x</c> itself.
/// </remarks>
internal sealed class ProjectionNode(
    QueryNode parent,
    IReadOnlyList<string> propertyNames,
    IReadOnlyList<string> entityPaths,
    bool readsTheSqlColumns = false,
    IReadOnlyList<ValueTemplate>? values = null,
    IReadOnlyList<object?>? valueSlots = null)
    : QueryNode(parent)
{
    /// <summary>
    /// Whether this is the projection <c>Database.SqlQuery</c> reads its results with, which doesn't compose over the SQL.
    /// </summary>
    public bool ReadsTheSqlColumns { get; } = readsTheSqlColumns;

    /// <summary>The property paths the reader reads, in the order of its ordinals. A path can repeat.</summary>
    public IReadOnlyList<string> PropertyNames { get; } = propertyNames;

    /// <summary>The columns the statement selects first: <see cref="PropertyNames"/> once each, in first-seen order.</summary>
    public IReadOnlyList<string> Columns { get; } = [.. propertyNames.Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The navigation paths of the entities the reader uses, in the order of its entities. A path can repeat; each
    /// occurrence is materialized as EF Core does, which without tracking or identity resolution is its own instance.
    /// </summary>
    public IReadOnlyList<string> EntityPaths { get; } = entityPaths;

    /// <summary>
    /// The values computed in SQL that the reader reads after <see cref="Columns"/>, in the order of its ordinals,
    /// such as <c>x.Posts.Count()</c>.
    /// </summary>
    public IReadOnlyList<ValueTemplate> Values { get; } = values ?? [];

    /// <summary>The value slots of <see cref="Values"/>.</summary>
    public IReadOnlyList<object?> ValueSlots { get; } = valueSlots ?? [];
}
