namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>Marks the query as <c>AsNoTracking</c>.</summary>
internal sealed class NoTrackingNode(QueryNode parent) : QueryNode(parent);
