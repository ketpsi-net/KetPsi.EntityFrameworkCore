using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The query an intercepted <c>GroupBy</c> returns: the operator chain of the grouped entity query. <c>Where</c> and
/// <c>OrderBy</c> on the groups, a <c>Select</c> of keys and aggregates, and <c>Count</c> or <c>Any</c> of the groups
/// can follow it.
/// </summary>
internal sealed class GroupedQueryable<TKey, TElement>(DbContext context, QueryNode tail)
    : IOrderedQueryable<IGrouping<TKey, TElement>>, IAsyncEnumerable<IGrouping<TKey, TElement>>
{
    public DbContext Context { get; } = context;

    public QueryNode Tail { get; } = tail;

    public Type ElementType => typeof(IGrouping<TKey, TElement>);

    public Expression Expression => Expression.Constant(this);

    public IQueryProvider Provider => SqlQueryProvider.Instance;

    /// <summary>Returns a new query over the same context whose chain ends at <paramref name="newTail"/>.</summary>
    public GroupedQueryable<TKey, TElement> WithTail(QueryNode newTail) => new(Context, newTail);

    public IEnumerator<IGrouping<TKey, TElement>> GetEnumerator() => throw new NotSupportedException(ExceptionMessages.GroupEnumerationNotSupported);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<IGrouping<TKey, TElement>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(ExceptionMessages.GroupEnumerationNotSupported);
}
