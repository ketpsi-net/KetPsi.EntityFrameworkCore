using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The <see cref="IQueryable{T}"/> that intercepted operators return. It carries the
/// <see cref="DbContext"/> and the tail of an immutable operator chain; it never builds
/// expression trees, and its provider rejects any operator that was not intercepted.
/// </summary>
internal sealed class SqlQueryable<T>(DbContext context, QueryNode tail) : IOrderedQueryable<T>
{
    public DbContext Context { get; } = context;

    public QueryNode Tail { get; } = tail;

    public Type ElementType => typeof(T);

    public Expression Expression => Expression.Constant(this);

    public IQueryProvider Provider => SqlQueryProvider.Instance;

    /// <summary>Returns a new query over the same context whose chain ends at <paramref name="newTail"/>.</summary>
    public SqlQueryable<T> WithTail(QueryNode newTail) => new(Context, newTail);

    public IEnumerator<T> GetEnumerator() => throw new NotSupportedException(ExceptionMessages.EnumerationNotSupported);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
