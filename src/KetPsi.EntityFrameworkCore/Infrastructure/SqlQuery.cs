using System.ComponentModel;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
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

    /// <summary>Appends <c>Where</c>.</summary>
    /// <param name="source">The query.</param>
    /// <param name="predicate">The predicate translated at build time.</param>
    /// <param name="values">The predicate's value slots, read from the lambda when the operator ran. The query takes ownership of the array.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not have one entry per value slot.</exception>
    public static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, object?[] values)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != predicate.ValueCount)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ValueCountMismatch, predicate.ValueCount, values.Length),
                nameof(values));
        }

        var query = ToSqlQueryable(source);
        return query.WithTail(new WhereNode(query.Tail, predicate, values));
    }

    /// <summary>Appends <c>Where</c> when <paramref name="condition"/> is <see langword="true"/>; otherwise returns the query unchanged.</summary>
    /// <param name="source">The query.</param>
    /// <param name="condition">Whether to apply the predicate.</param>
    /// <param name="predicate">The predicate translated at build time.</param>
    /// <param name="values">The predicate's value slots, read from the lambda when the operator ran. The query takes ownership of the array.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not have one entry per value slot.</exception>
    public static IQueryable<T> WhereIf<T>(IQueryable<T> source, bool condition, PredicateTemplate predicate, object?[] values)
        where T : class
        => condition ? Where(source, predicate, values) : ToSqlQueryable(source);

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

    /// <summary>Executes the query and returns the entities: <c>ToList</c>.</summary>
    public static List<T> ToList<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.ToList(ToSqlQueryable(source));

    /// <summary>Executes the query and returns the entities: <c>ToListAsync</c>.</summary>
    public static Task<List<T>> ToListAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.ToListAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Executes the query and returns the entities: <c>ToArray</c>.</summary>
    public static T[] ToArray<T>(IQueryable<T> source)
        where T : class
        => [.. QueryRunner.ToList(ToSqlQueryable(source))];

    /// <summary>Executes the query and returns the entities: <c>ToArrayAsync</c>.</summary>
    public static async Task<T[]> ToArrayAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => [.. await QueryRunner.ToListAsync(ToSqlQueryable(source), cancellationToken).ConfigureAwait(false)];

    /// <summary>Returns the first entity: <c>First</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows.</exception>
    public static T First<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.First(ToSqlQueryable(source));

    /// <summary>Returns the first entity: <c>FirstAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows.</exception>
    public static Task<T> FirstAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.FirstAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the first entity, or <see langword="null"/>: <c>FirstOrDefault</c>.</summary>
    public static T? FirstOrDefault<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.FirstOrDefault(ToSqlQueryable(source));

    /// <summary>Returns the first entity, or <see langword="null"/>: <c>FirstOrDefaultAsync</c>.</summary>
    public static Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.FirstOrDefaultAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the only entity: <c>Single</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows, or more than one.</exception>
    public static T Single<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Single(ToSqlQueryable(source));

    /// <summary>Returns the only entity: <c>SingleAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows, or more than one.</exception>
    public static Task<T> SingleAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.SingleAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the only entity, or <see langword="null"/>: <c>SingleOrDefault</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns more than one row.</exception>
    public static T? SingleOrDefault<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.SingleOrDefault(ToSqlQueryable(source));

    /// <summary>Returns the only entity, or <see langword="null"/>: <c>SingleOrDefaultAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns more than one row.</exception>
    public static Task<T?> SingleOrDefaultAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.SingleOrDefaultAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Counts the rows: <c>Count</c>.</summary>
    public static int Count<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Count(ToSqlQueryable(source));

    /// <summary>Counts the rows: <c>CountAsync</c>.</summary>
    public static Task<int> CountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.CountAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Counts the rows: <c>LongCount</c>.</summary>
    public static long LongCount<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.LongCount(ToSqlQueryable(source));

    /// <summary>Counts the rows: <c>LongCountAsync</c>.</summary>
    public static Task<long> LongCountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.LongCountAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns whether any row exists: <c>Any</c>.</summary>
    public static bool Any<T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Any(ToSqlQueryable(source));

    /// <summary>Returns whether any row exists: <c>AnyAsync</c>.</summary>
    public static Task<bool> AnyAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.AnyAsync(ToSqlQueryable(source), cancellationToken);

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
