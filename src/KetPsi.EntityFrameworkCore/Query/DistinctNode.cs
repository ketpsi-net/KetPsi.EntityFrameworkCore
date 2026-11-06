namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Distinct</c>: removes duplicate rows of the query so far. Like EF Core, an ordering before it is dropped, since
/// distinct rows have no defined order.
/// </summary>
internal sealed class DistinctNode(QueryNode parent) : QueryNode(parent);
