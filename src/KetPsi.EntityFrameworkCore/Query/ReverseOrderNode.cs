namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// Reverses the ordering of the query so far, so its first row is the last one: how <c>Last</c> and
/// <c>LastOrDefault</c> run, as in EF Core. A query without an ordering has no last row, and is rejected.
/// </summary>
/// <param name="parent">The node before it.</param>
/// <param name="method">The operator that reverses the query, <c>Last</c> or <c>LastOrDefault</c>, for the error message.</param>
internal sealed class ReverseOrderNode(QueryNode parent, string method) : QueryNode(parent)
{
    public string Method { get; } = method;
}
