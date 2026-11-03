using System.ComponentModel;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The operators and terminals generated interceptors call on a query projected by <see cref="SqlQuery.Select"/>.
/// Unlike <see cref="SqlQuery"/>, the element type is unconstrained: it can be a value type or an anonymous type.
/// Applications don't call this.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SqlProjection
{
    /// <summary>Whether <paramref name="source"/> is a projected KetPsi query; otherwise the interceptor calls the original operator.</summary>
    public static bool IsInterceptable<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source is ProjectedQueryable<T>;
    }

    /// <summary>Appends <c>Skip</c>.</summary>
    public static IQueryable<T> Skip<T>(IQueryable<T> source, int count)
    {
        var query = ToProjected(source);
        return query.WithTail(new SkipNode(query.Tail, count));
    }

    /// <summary>Appends <c>Take</c>.</summary>
    public static IQueryable<T> Take<T>(IQueryable<T> source, int count)
    {
        var query = ToProjected(source);
        return query.WithTail(new TakeNode(query.Tail, count));
    }

    /// <summary>Appends <c>Distinct</c>: duplicate projected rows are removed, and an ordering before it is dropped.</summary>
    public static IQueryable<T> Distinct<T>(IQueryable<T> source)
    {
        var query = ToProjected(source);
        return query.WithTail(new DistinctNode(query.Tail));
    }

    /// <summary>Executes the query: <c>ToList</c>.</summary>
    public static List<T> ToList<T>(IQueryable<T> source) => ProjectionRunner.ToList(ToProjected(source));

    /// <summary>Executes the query: <c>ToListAsync</c>.</summary>
    public static Task<List<T>> ToListAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.ToListAsync(ToProjected(source), cancellationToken);

    /// <summary>Executes the query: <c>ToArray</c>.</summary>
    public static T[] ToArray<T>(IQueryable<T> source) => [.. ProjectionRunner.ToList(ToProjected(source))];

    /// <summary>Executes the query: <c>ToArrayAsync</c>.</summary>
    public static async Task<T[]> ToArrayAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => [.. await ProjectionRunner.ToListAsync(ToProjected(source), cancellationToken).ConfigureAwait(false)];

    /// <summary>Returns the first result: <c>First</c>.</summary>
    public static T First<T>(IQueryable<T> source) => ProjectionRunner.First(ToProjected(source));

    /// <summary>Returns the first result: <c>FirstAsync</c>.</summary>
    public static Task<T> FirstAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.FirstAsync(ToProjected(source), cancellationToken);

    /// <summary>Returns the first result or the default: <c>FirstOrDefault</c>.</summary>
    public static T? FirstOrDefault<T>(IQueryable<T> source) => ProjectionRunner.FirstOrDefault(ToProjected(source));

    /// <summary>Returns the first result or the default: <c>FirstOrDefaultAsync</c>.</summary>
    public static Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.FirstOrDefaultAsync(ToProjected(source), cancellationToken);

    /// <summary>Returns the only result: <c>Single</c>.</summary>
    public static T Single<T>(IQueryable<T> source) => ProjectionRunner.Single(ToProjected(source));

    /// <summary>Returns the only result: <c>SingleAsync</c>.</summary>
    public static Task<T> SingleAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.SingleAsync(ToProjected(source), cancellationToken);

    /// <summary>Returns the only result or the default: <c>SingleOrDefault</c>.</summary>
    public static T? SingleOrDefault<T>(IQueryable<T> source) => ProjectionRunner.SingleOrDefault(ToProjected(source));

    /// <summary>Returns the only result or the default: <c>SingleOrDefaultAsync</c>.</summary>
    public static Task<T?> SingleOrDefaultAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.SingleOrDefaultAsync(ToProjected(source), cancellationToken);

    /// <summary>Counts the results: <c>Count</c>.</summary>
    public static int Count<T>(IQueryable<T> source) => ProjectionRunner.Count(ToProjected(source));

    /// <summary>Counts the results: <c>CountAsync</c>.</summary>
    public static Task<int> CountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.CountAsync(ToProjected(source), cancellationToken);

    /// <summary>Counts the results: <c>LongCount</c>.</summary>
    public static long LongCount<T>(IQueryable<T> source) => ProjectionRunner.LongCount(ToProjected(source));

    /// <summary>Counts the results: <c>LongCountAsync</c>.</summary>
    public static Task<long> LongCountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.LongCountAsync(ToProjected(source), cancellationToken);

    /// <summary>Whether there is any result: <c>Any</c>.</summary>
    public static bool Any<T>(IQueryable<T> source) => ProjectionRunner.Any(ToProjected(source));

    /// <summary>Whether there is any result: <c>AnyAsync</c>.</summary>
    public static Task<bool> AnyAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.AnyAsync(ToProjected(source), cancellationToken);

    internal static ProjectedQueryable<T> ToProjected<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source as ProjectedQueryable<T>
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NotAProjectedQuery, source.GetType()));
    }
}
