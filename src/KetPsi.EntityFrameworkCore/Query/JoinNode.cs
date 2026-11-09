namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// <c>Join</c>: an <c>INNER JOIN</c> of another entity query, the inner one, on keys of both. The joined entity is read
/// at the join path <c>"$0"</c> for the chain's first join, <c>"$1"</c> for the next, and so on, which the paths after
/// it start with, such as <c>"$0.Name"</c>.
/// </summary>
/// <param name="parent">The node before it.</param>
/// <param name="inner">The tail of the inner query: a root and its <c>Where</c> and tracking nodes.</param>
/// <param name="outerKeys">The paths of the outer key parts, which read the queried entity or an earlier join.</param>
/// <param name="innerKeys">The inner entity's key properties, one for each outer key part, in the same order.</param>
internal sealed class JoinNode(QueryNode parent, QueryNode inner, IReadOnlyList<string> outerKeys, IReadOnlyList<string> innerKeys)
    : QueryNode(parent)
{
    /// <summary>The prefix of a join path.</summary>
    public const char PathPrefix = '$';

    public QueryNode Inner { get; } = inner;

    public IReadOnlyList<string> OuterKeys { get; } = outerKeys;

    public IReadOnlyList<string> InnerKeys { get; } = innerKeys;

    /// <summary>Whether a path is a join path itself, such as <c>"$0"</c>, rather than one that reads through it.</summary>
    public static bool IsJoinPath(string path) => path.Length > 1 && path[0] == PathPrefix && !path.Contains('.', StringComparison.Ordinal);
}
