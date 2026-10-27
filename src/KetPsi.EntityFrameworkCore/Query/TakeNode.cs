namespace KetPsi.EntityFrameworkCore.Query;

internal sealed class TakeNode(QueryNode parent, int count) : QueryNode(parent)
{
    public int Count { get; } = count;
}
