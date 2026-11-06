namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>GroupBy</c>: the rows the operators before it selected are grouped by the key columns. Operators after it work
/// on the groups; when the groups themselves are returned, each row is read as its key and its element.
/// </summary>
/// <remarks>
/// A path starts at the queried entity and follows reference navigations, as in a projection: <c>"Status"</c> is
/// <c>x.Status</c>, and <c>"Blog.Name"</c> is <c>x.Blog.Name</c>. A composite key has one path per member.
/// </remarks>
/// <param name="parent">The previous node.</param>
/// <param name="keyPaths">The property paths of the key.</param>
/// <param name="elementPropertyNames">The property paths the element reader reads, in the order of its ordinals.</param>
/// <param name="elementEntityPaths">The navigation paths of the entities the element reader uses: <c>""</c> for the grouped entity.</param>
internal sealed class GroupByNode(
    QueryNode parent, IReadOnlyList<string> keyPaths, IReadOnlyList<string> elementPropertyNames, IReadOnlyList<string> elementEntityPaths)
    : QueryNode(parent)
{
    public IReadOnlyList<string> KeyPaths { get; } = keyPaths;

    public IReadOnlyList<string> ElementPropertyNames { get; } = elementPropertyNames;

    public IReadOnlyList<string> ElementEntityPaths { get; } = elementEntityPaths;
}
