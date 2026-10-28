namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// Loads related entities along a navigation path: <c>Include(b =&gt; b.Posts)</c> is <c>["Posts"]</c>, and a
/// following <c>ThenInclude(p =&gt; p.Comments)</c> is <c>["Posts", "Comments"]</c>.
/// </summary>
internal sealed class IncludeNode(QueryNode parent, IReadOnlyList<string> path) : QueryNode(parent)
{
    /// <summary>The navigation names from the queried entity, in order. Never empty.</summary>
    public IReadOnlyList<string> Path { get; } = path;
}
