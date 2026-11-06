using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The query an intercepted <c>GroupBy</c> returns: the operator chain of the grouped entity query and the generated
/// readers of each row's key and element. <c>Where</c> and <c>OrderBy</c> on the groups, a <c>Select</c> of keys and
/// aggregates, and <c>Count</c> or <c>Any</c> of the groups can follow it; enumerating it returns the groups.
/// </summary>
internal sealed class GroupedQueryable<TKey, TElement>(
    DbContext context, QueryNode tail, ProjectionReader<TKey> keyReader, ProjectionReader<TElement> elementReader)
    : IOrderedQueryable<IGrouping<TKey, TElement>>, IAsyncEnumerable<IGrouping<TKey, TElement>>, IGroupedQueryable
{
    public DbContext Context { get; } = context;

    public QueryNode Tail { get; } = tail;

    /// <summary>Reads the key from the row: its ordinals are those of the key paths, in order.</summary>
    public ProjectionReader<TKey> KeyReader { get; } = keyReader;

    /// <summary>Reads the element from the row: the grouped entity, or what the element selector builds.</summary>
    public ProjectionReader<TElement> ElementReader { get; } = elementReader;

    /// <summary>The <c>GroupBy</c> the groups come from.</summary>
    public GroupByNode Group
    {
        get
        {
            for (var node = Tail; node is not null; node = node.Parent)
            {
                if (node is GroupByNode group)
                {
                    return group;
                }
            }

            throw new UnreachableException("A grouped query always has a group node.");
        }
    }

    public Type ElementType => typeof(IGrouping<TKey, TElement>);

    public Expression Expression => Expression.Constant(this);

    public IQueryProvider Provider => SqlQueryProvider.Instance;

    /// <summary>Returns a new query over the same context and readers whose chain ends at <paramref name="newTail"/>.</summary>
    public GroupedQueryable<TKey, TElement> WithTail(QueryNode newTail) => new(Context, newTail, KeyReader, ElementReader);

    IQueryable IGroupedQueryable.WithTail(QueryNode newTail) => WithTail(newTail);

    IList IGroupedQueryable.ToList() => GroupRunner.ToList(this);

    async Task<IList> IGroupedQueryable.ToListAsync(CancellationToken cancellationToken)
        => await GroupRunner.ToListAsync(this, cancellationToken).ConfigureAwait(false);

    public IEnumerator<IGrouping<TKey, TElement>> GetEnumerator() => GroupRunner.Enumerate(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<IGrouping<TKey, TElement>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => GroupRunner.EnumerateAsync(this, cancellationToken).GetAsyncEnumerator(cancellationToken);
}

/// <summary>
/// A grouped query whatever its key and element types, which generated interceptors may not be able to name: an
/// anonymous key makes the grouping type unnameable.
/// </summary>
internal interface IGroupedQueryable
{
    DbContext Context { get; }

    QueryNode Tail { get; }

    /// <summary>Returns a new query of the same type whose chain ends at <paramref name="newTail"/>.</summary>
    IQueryable WithTail(QueryNode newTail);

    /// <summary>Returns the groups, a <c>List&lt;IGrouping&lt;TKey, TElement&gt;&gt;</c>.</summary>
    IList ToList();

    /// <summary>Returns the groups asynchronously, a <c>List&lt;IGrouping&lt;TKey, TElement&gt;&gt;</c>.</summary>
    Task<IList> ToListAsync(CancellationToken cancellationToken);
}
