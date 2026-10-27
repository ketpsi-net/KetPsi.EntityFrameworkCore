using System.ComponentModel;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EfAsyncQueryProvider = Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Entry points for the interceptors emitted by KetPsi.EntityFrameworkCore.Generator.
/// Application code should keep using LINQ and never call these members directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SqlQuery
{
    /// <summary>
    /// Decides whether an intercepted operator composes a KetPsi query or falls back to the original LINQ operator.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="source"/> is a <see cref="DbSet{TEntity}"/> or a KetPsi query;
    /// <see langword="false"/> when it is some other queryable, such as LINQ to Objects.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="source"/> is an EF Core query composed by an operator that was not intercepted.
    /// </exception>
    public static bool IsInterceptable<T>(IQueryable<T> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is SqlQueryable<T> or DbSet<T>)
        {
            return true;
        }

        if (source.Provider is EfAsyncQueryProvider)
        {
            throw new InvalidOperationException(ExceptionMessages.UninterceptedEntityFrameworkOperator);
        }

        return false;
    }

    /// <summary>Appends <c>AsNoTracking</c>.</summary>
    public static IQueryable<T> AsNoTracking<T>(IQueryable<T> source)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new NoTrackingNode(query.Tail));
    }

    /// <summary>Appends <c>IgnoreQueryFilters()</c>, which disables every model query filter.</summary>
    public static IQueryable<T> IgnoreQueryFilters<T>(IQueryable<T> source)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new IgnoreQueryFiltersNode(query.Tail, filterKeys: null));
    }

    /// <summary>Appends <c>IgnoreQueryFilters(filterKeys)</c>, which disables only the named query filters.</summary>
    public static IQueryable<T> IgnoreQueryFilters<T>(IQueryable<T> source, IReadOnlyCollection<string> filterKeys)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(filterKeys);

        var query = ToSqlQueryable(source);
        return query.WithTail(new IgnoreQueryFiltersNode(query.Tail, [.. filterKeys]));
    }

    /// <summary>Appends <c>OrderBy</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: false, isThenBy: false);

    /// <summary>Appends <c>OrderByDescending</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> OrderByDescending<T>(IQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: true, isThenBy: false);

    /// <summary>Appends <c>ThenBy</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> ThenBy<T>(IOrderedQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: false, isThenBy: true);

    /// <summary>Appends <c>ThenByDescending</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> ThenByDescending<T>(IOrderedQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: true, isThenBy: true);

    /// <summary>Appends <c>Skip</c>.</summary>
    public static IQueryable<T> Skip<T>(IQueryable<T> source, int count)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new SkipNode(query.Tail, count));
    }

    /// <summary>Appends <c>Take</c>.</summary>
    public static IQueryable<T> Take<T>(IQueryable<T> source, int count)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new TakeNode(query.Tail, count));
    }

    internal static SqlQueryable<T> ToSqlQueryable<T>(IQueryable<T> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return source switch
        {
            SqlQueryable<T> query => query,
            DbSet<T> set => new SqlQueryable<T>(set.GetService<ICurrentDbContext>().Context, new RootNode(typeof(T))),
            _ => throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NotAnEntityQuery, source.GetType())),
        };
    }

    private static SqlQueryable<T> AppendOrder<T>(IQueryable<T> source, string propertyName, bool descending, bool isThenBy)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        var query = ToSqlQueryable(source);
        return query.WithTail(new OrderNode(query.Tail, propertyName, descending, isThenBy));
    }
}
