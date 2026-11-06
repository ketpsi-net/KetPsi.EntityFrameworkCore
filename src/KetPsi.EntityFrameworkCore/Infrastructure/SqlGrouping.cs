using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The operators and terminals generated interceptors call for <c>GroupBy</c>: grouping an entity query, filtering and
/// ordering the groups, selecting their keys and aggregates, counting them, and returning the groups themselves.
/// Applications don't call this.
/// </summary>
/// <remarks>
/// The statement groups with <c>GROUP BY</c>, filters groups with <c>HAVING</c> and orders them by key parts and
/// aggregates, as EF Core does. The values a predicate, an ordering or a <c>Select</c> reads are
/// <see cref="GroupValue"/>s; the value slots of their count predicates come with each call, like a <c>Where</c>'s.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SqlGrouping
{
    /// <summary>Whether <paramref name="source"/> is a grouped KetPsi query; otherwise the interceptor calls the original operator.</summary>
    /// <typeparam name="T">The grouping type, <c>IGrouping&lt;TKey, TElement&gt;</c>, which generated code may not be able to name.</typeparam>
    public static bool IsInterceptable<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source is IGroupedQueryable;
    }

    /// <summary>
    /// Appends <c>GroupBy</c>, with or without an element selector: the rows are grouped by the key paths. When the
    /// groups themselves are returned, each row is read with <paramref name="keyReader"/> and
    /// <paramref name="elementReader"/>.
    /// </summary>
    /// <param name="source">The entity query.</param>
    /// <param name="keyPaths">
    /// The property paths of the key, one per member of a composite key: <c>"Status"</c> for <c>x =&gt; x.Status</c>,
    /// or <c>"Blog.Name"</c> for <c>x =&gt; x.Blog.Name</c> through a reference navigation.
    /// </param>
    /// <param name="keyReader">The generated key reader: its ordinals are those of <paramref name="keyPaths"/>, in order.</param>
    /// <param name="elementPropertyNames">The property paths the element reader reads, in the order of its ordinals.</param>
    /// <param name="elementEntityPaths">
    /// The navigation paths of the entities the element reader uses, in the order of its entities: <c>[""]</c> for
    /// the grouped entity itself when there is no element selector.
    /// </param>
    /// <param name="elementReader">The generated element reader: the element selector's body, or the grouped entity.</param>
    /// <exception cref="ArgumentException"><paramref name="keyPaths"/> is empty or has an empty path.</exception>
    public static IQueryable<IGrouping<TKey, TElement>> GroupBy<[DynamicallyAccessedMembers(EntityMembers.Required)] TSource, TKey, TElement>(
        IQueryable<TSource> source,
        IReadOnlyList<string> keyPaths,
        ProjectionReader<TKey> keyReader,
        IReadOnlyList<string> elementPropertyNames,
        IReadOnlyList<string> elementEntityPaths,
        ProjectionReader<TElement> elementReader)
        where TSource : class
    {
        ArgumentNullException.ThrowIfNull(keyPaths);
        ArgumentNullException.ThrowIfNull(keyReader);
        ArgumentNullException.ThrowIfNull(elementPropertyNames);
        ArgumentNullException.ThrowIfNull(elementEntityPaths);
        ArgumentNullException.ThrowIfNull(elementReader);
        if (keyPaths.Count == 0)
        {
            throw new ArgumentException(ExceptionMessages.GroupKeyRequired, nameof(keyPaths));
        }

        foreach (var path in keyPaths)
        {
            ArgumentException.ThrowIfNullOrEmpty(path, nameof(keyPaths));
        }

        var query = SqlQuery.ToSqlQueryable(source);
        var group = new GroupByNode(query.Tail, [.. keyPaths], [.. elementPropertyNames], [.. elementEntityPaths]);
        return new GroupedQueryable<TKey, TElement>(query.Context, group, keyReader, elementReader);
    }

    /// <summary>Appends <c>Where</c> on the groups: the predicate becomes a <c>HAVING</c> condition.</summary>
    /// <param name="source">The grouped query.</param>
    /// <param name="predicate">The predicate over <paramref name="operands"/>: property name <c>"0"</c> is <c>operands[0]</c>.</param>
    /// <param name="operands">The key parts and aggregates the predicate compares.</param>
    /// <param name="values">The value slots of the predicate and the operands' count predicates. The query takes ownership of the array.</param>
    public static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, IReadOnlyList<GroupValue> operands, object?[] values)
    {
        var query = ToGrouped(source);
        return (IQueryable<T>)query.WithTail(CreateHaving(query.Tail, predicate, operands, values));
    }

    /// <summary>Appends <c>OrderBy</c> on a key part or aggregate of the groups.</summary>
    public static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, GroupValue value, object?[] values)
        => AppendOrder(source, value, values, descending: false, isThenBy: false);

    /// <summary>Appends <c>OrderByDescending</c> on a key part or aggregate of the groups.</summary>
    public static IOrderedQueryable<T> OrderByDescending<T>(IQueryable<T> source, GroupValue value, object?[] values)
        => AppendOrder(source, value, values, descending: true, isThenBy: false);

    /// <summary>Appends <c>ThenBy</c> on a key part or aggregate of the groups.</summary>
    public static IOrderedQueryable<T> ThenBy<T>(IOrderedQueryable<T> source, GroupValue value, object?[] values)
        => AppendOrder(source, value, values, descending: false, isThenBy: true);

    /// <summary>Appends <c>ThenByDescending</c> on a key part or aggregate of the groups.</summary>
    public static IOrderedQueryable<T> ThenByDescending<T>(IOrderedQueryable<T> source, GroupValue value, object?[] values)
        => AppendOrder(source, value, values, descending: true, isThenBy: true);

    /// <summary>
    /// Appends <c>Select</c> on the groups, or stands for the result selector of <c>GroupBy</c>: the query returns
    /// what <paramref name="reader"/> builds from each group's values.
    /// </summary>
    /// <param name="source">The grouped query.</param>
    /// <param name="values">The key parts and aggregates the reader reads; the ordinal of <c>values[i]</c> is <c>i</c>.</param>
    /// <param name="predicateValues">The value slots of the values' count predicates. The query takes ownership of the array.</param>
    /// <param name="reader">The generated reader: the <c>Select</c> lambda's body over the values.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty, or a predicate reads a missing value slot.</exception>
    public static IQueryable<TResult> Select<T, TResult>(
        IQueryable<T> source, IReadOnlyList<GroupValue> values, object?[] predicateValues, ProjectionReader<TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(predicateValues);
        ArgumentNullException.ThrowIfNull(reader);
        if (values.Count == 0)
        {
            throw new ArgumentException(ExceptionMessages.GroupValuesRequired, nameof(values));
        }

        ValidateValues(values, predicateValues);

        var query = ToGrouped(source);
        return new ProjectedQueryable<TResult>(query.Context, new GroupSelectNode(query.Tail, [.. values], predicateValues), reader);
    }

    /// <summary>Returns the groups: <c>ToList</c>. Rows are read ordered by the key and grouped where it changes.</summary>
    public static List<T> ToList<T>(IQueryable<T> source) => (List<T>)ToGrouped(source).ToList();

    /// <summary>Returns the groups: <c>ToListAsync</c>.</summary>
    public static async Task<List<T>> ToListAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => (List<T>)await ToGrouped(source).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the groups: <c>ToArray</c>.</summary>
    public static T[] ToArray<T>(IQueryable<T> source) => [.. ToList(source)];

    /// <summary>Returns the groups: <c>ToArrayAsync</c>.</summary>
    public static async Task<T[]> ToArrayAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => [.. await ToListAsync(source, cancellationToken).ConfigureAwait(false)];

    /// <summary>Counts the groups: <c>Count</c>.</summary>
    public static int Count<T>(IQueryable<T> source)
        => Convert.ToInt32(ExecuteScalar(source, QueryTerminal.Count), CultureInfo.InvariantCulture);

    /// <summary>Counts the groups: <c>CountAsync</c>.</summary>
    public static async Task<int> CountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => Convert.ToInt32(await ExecuteScalarAsync(source, QueryTerminal.Count, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Counts the groups: <c>LongCount</c>.</summary>
    public static long LongCount<T>(IQueryable<T> source)
        => Convert.ToInt64(ExecuteScalar(source, QueryTerminal.LongCount), CultureInfo.InvariantCulture);

    /// <summary>Counts the groups: <c>LongCountAsync</c>.</summary>
    public static async Task<long> LongCountAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => Convert.ToInt64(await ExecuteScalarAsync(source, QueryTerminal.LongCount, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Whether there is any group: <c>Any</c>.</summary>
    public static bool Any<T>(IQueryable<T> source)
        => Convert.ToBoolean(ExecuteScalar(source, QueryTerminal.Any), CultureInfo.InvariantCulture);

    /// <summary>Whether there is any group: <c>AnyAsync</c>.</summary>
    public static async Task<bool> AnyAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => Convert.ToBoolean(await ExecuteScalarAsync(source, QueryTerminal.Any, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Creates the node of a <c>Where</c> on groups, before or after the grouped <c>Select</c>.</summary>
    internal static HavingNode CreateHaving(QueryNode parent, PredicateTemplate predicate, IReadOnlyList<GroupValue> operands, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operands);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < predicate.ValueCount)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ValueCountMismatch, predicate.ValueCount, values.Length),
                nameof(values));
        }

        ValidateValues(operands, values);
        return new HavingNode(parent, predicate, [.. operands], values);
    }

    /// <summary>Creates the node of an ordering on groups, before or after the grouped <c>Select</c>.</summary>
    internal static GroupOrderNode CreateOrder(QueryNode parent, GroupValue value, object?[] values, bool descending, bool isThenBy)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(values);

        ValidateValues([value], values);
        return new GroupOrderNode(parent, value, values, descending, isThenBy);
    }

    private static void ValidateValues(IReadOnlyList<GroupValue> groupValues, object?[] values)
    {
        foreach (var value in groupValues)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(groupValues));
            if (value.Predicate is { } predicate && predicate.ValueCount > values.Length)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ValueCountMismatch, predicate.ValueCount, values.Length),
                    nameof(values));
            }
        }
    }

    private static IOrderedQueryable<T> AppendOrder<T>(IQueryable<T> source, GroupValue value, object?[] values, bool descending, bool isThenBy)
    {
        var query = ToGrouped(source);
        return (IOrderedQueryable<T>)query.WithTail(CreateOrder(query.Tail, value, values, descending, isThenBy));
    }

    private static object? ExecuteScalar<T>(IQueryable<T> source, QueryTerminal terminal)
    {
        var query = ToGrouped(source);
        return QueryRunner.ExecuteScalar(query.Context, query.Tail, terminal);
    }

    private static Task<object?> ExecuteScalarAsync<T>(IQueryable<T> source, QueryTerminal terminal, CancellationToken cancellationToken)
    {
        var query = ToGrouped(source);
        return QueryRunner.ExecuteScalarAsync(query.Context, query.Tail, terminal, cancellationToken);
    }

    private static IGroupedQueryable ToGrouped<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source as IGroupedQueryable
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NotAGroupedQuery, source.GetType()));
    }
}
