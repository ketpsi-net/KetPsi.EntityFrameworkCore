using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The query an intercepted <c>Select</c> returns. Its element can be any type, an anonymous type or a value type
/// included, so it cannot be a <see cref="SqlQueryable{T}"/>. It carries the operator chain of the entity query it was
/// projected from and the generated reader that builds each element. Enumerating it executes the query.
/// </summary>
internal sealed class ProjectedQueryable<T>(DbContext context, QueryNode tail, ProjectionReader<T> reader)
    : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public DbContext Context { get; } = context;

    public QueryNode Tail { get; } = tail;

    public ProjectionReader<T> Reader { get; } = reader;

    /// <summary>The <c>Select</c> the elements come from; operators after it can follow.</summary>
    public ProjectionNode Projection
    {
        get
        {
            for (var node = Tail; node is not null; node = node.Parent)
            {
                if (node is ProjectionNode projection)
                {
                    return projection;
                }
            }

            throw new UnreachableException("A projected query always has a projection node.");
        }
    }

    public Type ElementType => typeof(T);

    public Expression Expression => Expression.Constant(this);

    public IQueryProvider Provider => SqlQueryProvider.Instance;

    /// <summary>Returns a new query over the same context and reader whose chain ends at <paramref name="newTail"/>.</summary>
    public ProjectedQueryable<T> WithTail(QueryNode newTail) => new(Context, newTail, Reader);

    public IEnumerator<T> GetEnumerator() => ProjectionRunner.Enumerate(this).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => ProjectionRunner.EnumerateAsync(this, cancellationToken).GetAsyncEnumerator(cancellationToken);
}
