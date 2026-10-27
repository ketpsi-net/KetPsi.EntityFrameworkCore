namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>The start of every chain: the entity set the query reads from.</summary>
internal sealed class RootNode(Type entityType) : QueryNode(parent: null)
{
    public Type EntityType { get; } = entityType;
}
