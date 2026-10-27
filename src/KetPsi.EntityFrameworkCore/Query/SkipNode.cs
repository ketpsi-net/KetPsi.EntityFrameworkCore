namespace KetPsi.EntityFrameworkCore.Query;

internal sealed class SkipNode(QueryNode parent, int count) : QueryNode(parent)
{
    public int Count { get; } = count;
}
