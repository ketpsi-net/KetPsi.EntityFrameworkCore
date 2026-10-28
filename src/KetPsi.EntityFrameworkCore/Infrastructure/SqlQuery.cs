using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    public static bool IsInterceptable<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
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
    public static IQueryable<T> Where<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, PredicateTemplate predicate, object?[] values)
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
    public static IQueryable<T> WhereIf<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, bool condition, PredicateTemplate predicate, object?[] values)
        where T : class
        => condition ? Where(source, predicate, values) : ToSqlQueryable(source);

    /// <summary>Appends <c>AsNoTracking</c>.</summary>
    public static IQueryable<T> AsNoTracking<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => AppendTracking(source, QueryTrackingBehavior.NoTracking);

    /// <summary>Appends <c>AsNoTrackingWithIdentityResolution</c>: rows with the same key return one instance per execution.</summary>
    public static IQueryable<T> AsNoTrackingWithIdentityResolution<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => AppendTracking(source, QueryTrackingBehavior.NoTrackingWithIdentityResolution);

    /// <summary>Appends <c>AsTracking</c>: the entities are attached to the context's change tracker.</summary>
    public static IQueryable<T> AsTracking<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => AppendTracking(source, QueryTrackingBehavior.TrackAll);

    /// <summary>Appends <c>AsTracking(behavior)</c>.</summary>
    public static IQueryable<T> AsTracking<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, QueryTrackingBehavior behavior)
        where T : class
        => AppendTracking(source, behavior);

    private static SqlQueryable<T> AppendTracking<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, QueryTrackingBehavior behavior)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new TrackingNode(query.Tail, behavior));
    }

    /// <summary>Appends <c>IgnoreQueryFilters()</c>, which disables every model query filter.</summary>
    public static IQueryable<T> IgnoreQueryFilters<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new IgnoreQueryFiltersNode(query.Tail, filterKeys: null));
    }

    /// <summary>Appends <c>IgnoreQueryFilters(filterKeys)</c>, which disables only the named query filters.</summary>
    public static IQueryable<T> IgnoreQueryFilters<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, IReadOnlyCollection<string> filterKeys)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(filterKeys);

        var query = ToSqlQueryable(source);
        return query.WithTail(new IgnoreQueryFiltersNode(query.Tail, [.. filterKeys]));
    }

    /// <summary>Appends <c>OrderBy</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> OrderBy<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: false, isThenBy: false);

    /// <summary>Appends <c>OrderByDescending</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> OrderByDescending<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: true, isThenBy: false);

    /// <summary>Appends <c>ThenBy</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> ThenBy<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IOrderedQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: false, isThenBy: true);

    /// <summary>Appends <c>ThenByDescending</c> on the entity property <paramref name="propertyName"/>.</summary>
    public static IOrderedQueryable<T> ThenByDescending<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IOrderedQueryable<T> source, string propertyName)
        where T : class
        => AppendOrder(source, propertyName, descending: true, isThenBy: true);

    /// <summary>Appends <c>Skip</c>.</summary>
    public static IQueryable<T> Skip<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, int count)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new SkipNode(query.Tail, count));
    }

    /// <summary>Appends <c>Take</c>.</summary>
    public static IQueryable<T> Take<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, int count)
        where T : class
    {
        var query = ToSqlQueryable(source);
        return query.WithTail(new TakeNode(query.Tail, count));
    }

    /// <summary>Executes the query and returns the entities: <c>ToList</c>.</summary>
    public static List<T> ToList<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.ToList(ToSqlQueryable(source));

    /// <summary>Executes the query and returns the entities: <c>ToListAsync</c>.</summary>
    public static Task<List<T>> ToListAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.ToListAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Executes the query and returns the entities: <c>ToArray</c>.</summary>
    public static T[] ToArray<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => [.. QueryRunner.ToList(ToSqlQueryable(source))];

    /// <summary>Executes the query and returns the entities: <c>ToArrayAsync</c>.</summary>
    public static async Task<T[]> ToArrayAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => [.. await QueryRunner.ToListAsync(ToSqlQueryable(source), cancellationToken).ConfigureAwait(false)];

    /// <summary>Returns the first entity: <c>First</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows.</exception>
    public static T First<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.First(ToSqlQueryable(source));

    /// <summary>Returns the first entity: <c>FirstAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows.</exception>
    public static Task<T> FirstAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.FirstAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the first entity, or <see langword="null"/>: <c>FirstOrDefault</c>.</summary>
    public static T? FirstOrDefault<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.FirstOrDefault(ToSqlQueryable(source));

    /// <summary>Returns the first entity, or <see langword="null"/>: <c>FirstOrDefaultAsync</c>.</summary>
    public static Task<T?> FirstOrDefaultAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.FirstOrDefaultAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the only entity: <c>Single</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows, or more than one.</exception>
    public static T Single<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Single(ToSqlQueryable(source));

    /// <summary>Returns the only entity: <c>SingleAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns no rows, or more than one.</exception>
    public static Task<T> SingleAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.SingleAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns the only entity, or <see langword="null"/>: <c>SingleOrDefault</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns more than one row.</exception>
    public static T? SingleOrDefault<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.SingleOrDefault(ToSqlQueryable(source));

    /// <summary>Returns the only entity, or <see langword="null"/>: <c>SingleOrDefaultAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">The query returns more than one row.</exception>
    public static Task<T?> SingleOrDefaultAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.SingleOrDefaultAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Counts the rows: <c>Count</c>.</summary>
    public static int Count<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Count(ToSqlQueryable(source));

    /// <summary>Counts the rows: <c>CountAsync</c>.</summary>
    public static Task<int> CountAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.CountAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Counts the rows: <c>LongCount</c>.</summary>
    public static long LongCount<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.LongCount(ToSqlQueryable(source));

    /// <summary>Counts the rows: <c>LongCountAsync</c>.</summary>
    public static Task<long> LongCountAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.LongCountAsync(ToSqlQueryable(source), cancellationToken);

    /// <summary>Returns whether any row exists: <c>Any</c>.</summary>
    public static bool Any<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
        where T : class
        => QueryRunner.Any(ToSqlQueryable(source));

    /// <summary>Returns whether any row exists: <c>AnyAsync</c>.</summary>
    public static Task<bool> AnyAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        where T : class
        => QueryRunner.AnyAsync(ToSqlQueryable(source), cancellationToken);

    internal static SqlQueryable<T> ToSqlQueryable<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source)
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

    private static SqlQueryable<T> AppendOrder<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IQueryable<T> source, string propertyName, bool descending, bool isThenBy)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        var query = ToSqlQueryable(source);
        return query.WithTail(new OrderNode(query.Tail, propertyName, descending, isThenBy));
    }
}
