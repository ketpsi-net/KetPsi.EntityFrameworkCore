using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>The start of every chain: the entity set the query reads from, its table or the SQL it was given.</summary>
/// <param name="entityType">The queried entity type, or the result type of <c>Database.SqlQuery</c>.</param>
/// <param name="sql">The SQL of <c>FromSql</c> or <c>SqlQuery</c>, read instead of a table; <see langword="null"/> for the table.</param>
/// <param name="unmapped">
/// For <c>Database.SqlQuery</c>, the result type's columns as an entity the model doesn't have: no table, no key and no
/// query filters; <see langword="null"/> for an entity of the model.
/// </param>
/// <param name="readsFirstColumn">
/// For <c>Database.SqlQuery</c> of a scalar, whether SQL sent as it is reads its first column whatever its name, as
/// EF Core reads it; a composed query reads the column named <c>Value</c>.
/// </param>
internal sealed class RootNode(Type entityType, RawSql? sql = null, BoundEntity? unmapped = null, bool readsFirstColumn = false)
    : QueryNode(parent: null)
{
    public Type EntityType { get; } = entityType;

    public RawSql? Sql { get; } = sql;

    public BoundEntity? Unmapped { get; } = unmapped;

    public bool ReadsFirstColumn { get; } = readsFirstColumn;
}
