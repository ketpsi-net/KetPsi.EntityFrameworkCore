namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// One step in a query's operator chain. Nodes link to their parent and are never mutated,
/// so queries forked from a shared base never affect each other.
/// </summary>
internal abstract class QueryNode(QueryNode? parent)
{
    public QueryNode? Parent { get; } = parent;
}
