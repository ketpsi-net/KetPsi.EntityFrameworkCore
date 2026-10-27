using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Execution;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The <see cref="IQueryable{T}"/> that intercepted operators return. It carries the
/// <see cref="DbContext"/> and the tail of an immutable operator chain; it never builds
/// expression trees, and its provider rejects any operator that was not intercepted.
/// Enumerating it, synchronously or asynchronously, executes the query.
/// </summary>
internal sealed class SqlQueryable<T>(DbContext context, QueryNode tail) : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public DbContext Context { get; } = context;

    public QueryNode Tail { get; } = tail;

    /// <summary>The first node of the chain.</summary>
    public RootNode Root
    {
        get
        {
            var node = Tail;
            while (node.Parent is not null)
            {
                node = node.Parent;
            }

            return node as RootNode ?? throw new UnreachableException("A chain always starts with a root node.");
        }
    }

    public Type ElementType => typeof(T);

    public Expression Expression => Expression.Constant(this);

    public IQueryProvider Provider => SqlQueryProvider.Instance;

    /// <summary>Returns a new query over the same context whose chain ends at <paramref name="newTail"/>.</summary>
    public SqlQueryable<T> WithTail(QueryNode newTail) => new(Context, newTail);

    public IEnumerator<T> GetEnumerator() => QueryRunner.Enumerate(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => QueryRunner.EnumerateAsync(this, cancellationToken).GetAsyncEnumerator(cancellationToken);
}
