namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The rows of a projected collection whose foreign key is one of the keys of the parents already read, as an
/// included collection is loaded.
/// </summary>
/// <param name="parent">The previous node, the related entity's root.</param>
/// <param name="propertyNames">The related entity's properties that hold the parent's key, in key order.</param>
/// <param name="keys">The distinct keys of the parents, each in key order. Never empty.</param>
internal sealed class ParentKeysNode(QueryNode parent, IReadOnlyList<string> propertyNames, IReadOnlyCollection<IReadOnlyList<object?>> keys)
    : QueryNode(parent)
{
    public IReadOnlyList<string> PropertyNames { get; } = propertyNames;

    public IReadOnlyCollection<IReadOnlyList<object?>> Keys { get; } = keys;
}
