namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>GroupBy</c>: the rows the operators before it selected are grouped by the key columns. Operators after it work
/// on the groups.
/// </summary>
/// <remarks>
/// A key path starts at the queried entity and follows reference navigations, as in a projection: <c>"Status"</c> is
/// <c>x.Status</c>, and <c>"Blog.Name"</c> is <c>x.Blog.Name</c>. A composite key has one path per member.
/// </remarks>
internal sealed class GroupByNode(QueryNode parent, IReadOnlyList<string> keyPaths) : QueryNode(parent)
{
    public IReadOnlyList<string> KeyPaths { get; } = keyPaths;
}
