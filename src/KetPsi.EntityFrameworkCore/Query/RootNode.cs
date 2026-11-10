namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>The start of every chain: the entity set the query reads from, its table or the SQL it was given.</summary>
/// <param name="entityType">The queried entity type.</param>
/// <param name="sql">The SQL of <c>FromSql</c>, read instead of the entity's table; <see langword="null"/> for the table.</param>
internal sealed class RootNode(Type entityType, RawSql? sql = null) : QueryNode(parent: null)
{
    public Type EntityType { get; } = entityType;

    public RawSql? Sql { get; } = sql;
}
