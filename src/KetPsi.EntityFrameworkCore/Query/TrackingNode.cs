using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary><c>AsTracking</c> or <c>AsNoTracking</c>. As in EF Core, the last one in the chain decides.</summary>
internal sealed class TrackingNode(QueryNode parent, QueryTrackingBehavior behavior) : QueryNode(parent)
{
    public QueryTrackingBehavior Behavior { get; } = behavior;
}
