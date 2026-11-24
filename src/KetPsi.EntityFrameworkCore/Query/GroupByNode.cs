using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

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
/// <param name="keyValues">
/// For each key part, the value it is computed from, such as <c>x.CreatedAt.Year</c>, or <see langword="null"/> for the
/// property at its path; <see langword="null"/> when every part is a property.
/// </param>
/// <param name="keySlots">The value slots of <paramref name="keyValues"/>.</param>
internal sealed class GroupByNode(
    QueryNode parent,
    IReadOnlyList<string> keyPaths,
    IReadOnlyList<string> elementPropertyNames,
    IReadOnlyList<string> elementEntityPaths,
    IReadOnlyList<ValueTemplate?>? keyValues = null,
    IReadOnlyList<object?>? keySlots = null)
    : QueryNode(parent)
{
    public IReadOnlyList<string> KeyPaths { get; } = keyPaths;

    /// <summary>For each key part, its computed value, or <see langword="null"/> for the property at its path.</summary>
    public IReadOnlyList<ValueTemplate?>? KeyValues { get; } = keyValues;

    /// <summary>The value slots of <see cref="KeyValues"/>.</summary>
    public IReadOnlyList<object?> KeySlots { get; } = keySlots ?? [];

    public IReadOnlyList<string> ElementPropertyNames { get; } = elementPropertyNames;

    public IReadOnlyList<string> ElementEntityPaths { get; } = elementEntityPaths;
}
