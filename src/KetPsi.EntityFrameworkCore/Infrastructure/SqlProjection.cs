using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <summary>
    /// Appends <c>Where</c> after a <c>Select</c> on groups: the predicate over the key parts and aggregates the
    /// filtered members read becomes a <c>HAVING</c> condition.
    /// </summary>
    /// <param name="source">The query projected from groups.</param>
    /// <param name="predicate">The predicate over <paramref name="operands"/>: property name <c>"0"</c> is <c>operands[0]</c>.</param>
    /// <param name="operands">The key parts and aggregates the predicate compares.</param>
    /// <param name="values">The value slots of the predicate and the operands' count predicates. The query takes ownership of the array.</param>
    public static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, IReadOnlyList<GroupValue> operands, object?[] values)
    {
        var query = ToProjected(source);
        return query.WithTail(SqlGrouping.CreateHaving(query.Tail, predicate, operands, values));
    }

    /// <summary>Appends <c>OrderBy</c> after a <c>Select</c> on groups, on the key part or aggregate the member reads.</summary>
    public static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, GroupValue value, object?[] values)
        => AppendGroupOrder(source, value, values, descending: false, isThenBy: false);

    /// <summary>Appends <c>OrderByDescending</c> after a <c>Select</c> on groups.</summary>
    public static IOrderedQueryable<T> OrderByDescending<T>(IQueryable<T> source, GroupValue value, object?[] values)
        => AppendGroupOrder(source, value, values, descending: true, isThenBy: false);

    /// <summary>Appends <c>ThenBy</c> after a <c>Select</c> on groups.</summary>
    public static IOrderedQueryable<T> ThenBy<T>(IOrderedQueryable<T> source, GroupValue value, object?[] values)
        => AppendGroupOrder(source, value, values, descending: false, isThenBy: true);

    /// <summary>Appends <c>ThenByDescending</c> after a <c>Select</c> on groups.</summary>
    public static IOrderedQueryable<T> ThenByDescending<T>(IOrderedQueryable<T> source, GroupValue value, object?[] values)
        => AppendGroupOrder(source, value, values, descending: true, isThenBy: true);

    /// <summary>
    /// Computes <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c> of a query projected to one property, such as
    /// <c>Select(x =&gt; x.Price).Sum()</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There are no rows and <typeparamref name="TResult"/> cannot be null.</exception>
    public static TResult Aggregate<T, TResult>(IQueryable<T> source, AggregateFunction function, AggregateValueType valueType)
    {
        var query = ToProjected(source);
        return AggregateExecutor.Execute<TResult>(query.Context, query.Tail, new SqlAggregate(function, PropertyName: null, valueType));
    }

    /// <summary>Computes the async form of <see cref="Aggregate"/>.</summary>
    public static Task<TResult> AggregateAsync<T, TResult>(
        IQueryable<T> source, AggregateFunction function, AggregateValueType valueType, CancellationToken cancellationToken = default)
    {
        var query = ToProjected(source);
        return AggregateExecutor.ExecuteAsync<TResult>(query.Context, query.Tail, new SqlAggregate(function, PropertyName: null, valueType), cancellationToken);
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

    /// <summary>Returns the last result in the query's ordering: <c>Last</c>.</summary>
    public static T Last<T>(IQueryable<T> source) => ProjectionRunner.Last(ToProjected(source));

    /// <summary>Returns the last result in the query's ordering: <c>LastAsync</c>.</summary>
    public static Task<T> LastAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.LastAsync(ToProjected(source), cancellationToken);

    /// <summary>Returns the last result in the query's ordering, or the default: <c>LastOrDefault</c>.</summary>
    public static T? LastOrDefault<T>(IQueryable<T> source) => ProjectionRunner.LastOrDefault(ToProjected(source));

    /// <summary>Returns the last result in the query's ordering, or the default: <c>LastOrDefaultAsync</c>.</summary>
    public static Task<T?> LastOrDefaultAsync<T>(IQueryable<T> source, CancellationToken cancellationToken = default)
        => ProjectionRunner.LastOrDefaultAsync(ToProjected(source), cancellationToken);

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

    /// <summary>
    /// Starts a query of a scalar or an unmapped type from SQL: <c>Database.SqlQuery</c>. The result columns are a
    /// scalar's <c>Value</c>, or the type's properties, which the operators after it read as any projection's.
    /// </summary>
    /// <param name="database">The context's database.</param>
    /// <param name="sql">The SQL; each interpolated value becomes a parameter.</param>
    /// <param name="columns">The result columns, which the reader reads in order.</param>
    /// <param name="columnTypes">The CLR type of each column, which types the parameters it is compared with.</param>
    /// <param name="readsFirstColumn">Whether the result is a scalar, read from the first column of SQL sent as it is.</param>
    /// <param name="reader">Builds each result.</param>
    public static IQueryable<T> SqlQuery<T>(
        DatabaseFacade database,
        FormattableString sql,
        IReadOnlyList<string> columns,
        IReadOnlyList<Type> columnTypes,
        bool readsFirstColumn,
        ProjectionReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(sql);

        return FromSqlColumns(database, new RawSql(sql.Format, sql.GetArguments(), alwaysFormat: true), columns, columnTypes, readsFirstColumn, reader);
    }

    /// <summary>Starts a query of a scalar or an unmapped type from SQL with placeholders: <c>Database.SqlQueryRaw</c>.</summary>
    public static IQueryable<T> SqlQueryRaw<T>(
        DatabaseFacade database,
        string sql,
        object?[]? parameters,
        IReadOnlyList<string> columns,
        IReadOnlyList<Type> columnTypes,
        bool readsFirstColumn,
        ProjectionReader<T> reader)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);

        return FromSqlColumns(database, new RawSql(sql, parameters ?? [], alwaysFormat: false), columns, columnTypes, readsFirstColumn, reader);
    }

    private static ProjectedQueryable<T> FromSqlColumns<T>(
        DatabaseFacade database, RawSql sql, IReadOnlyList<string> columns, IReadOnlyList<Type> columnTypes, bool readsFirstColumn, ProjectionReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(columnTypes);
        ArgumentNullException.ThrowIfNull(reader);
        if (columns.Count == 0 || columns.Count != columnTypes.Count)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.SqlQueryColumnsMismatch, columns.Count, columnTypes.Count), nameof(columnTypes));
        }

        var context = database.GetService<ICurrentDbContext>().Context;
        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var properties = new List<BoundProperty>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var type = columnTypes[i];
            var storeType = mappings.FindMapping(type)?.StoreType;
            var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
            properties.Add(new BoundProperty(columns[i], columns[i], storeType ?? "", nullable, type, IsShadow: false));
        }

        // An entity the model doesn't have, so it has no key, is never tracked and has no query filters, as in EF Core.
        var unmapped = new BoundEntity(typeof(T), table: "", schema: null, properties, primaryKey: null, filterKeys: []);
        var root = new RootNode(typeof(T), sql, unmapped, readsFirstColumn);
        return new ProjectedQueryable<T>(context, new ProjectionNode(root, columns, [], readsTheSqlColumns: true), reader);
    }

    /// <summary>
    /// Appends <c>Where</c> on a projection the generator reads through: the predicate's names are paths of the
    /// queried entity and of the joined ones, such as <c>"$0.Name"</c>, written through the projected members.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="values"/> does not have one entry per value slot.</exception>
    public static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != predicate.ValueCount)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ValueCountMismatch, predicate.ValueCount, values.Length),
                nameof(values));
        }

        var query = ToProjected(source);
        return query.WithTail(new WhereNode(query.Tail, predicate, values));
    }

    /// <summary>Appends <c>OrderBy</c> on a path of the queried or a joined entity, written through the projected members.</summary>
    public static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> source, string propertyPath)
        => AppendOrder(source, propertyPath, descending: false, isThenBy: false);

    /// <summary>Appends <c>OrderByDescending</c> on a path of the queried or a joined entity.</summary>
    public static IOrderedQueryable<T> OrderByDescending<T>(IQueryable<T> source, string propertyPath)
        => AppendOrder(source, propertyPath, descending: true, isThenBy: false);

    /// <summary>Appends <c>ThenBy</c> on a path of the queried or a joined entity.</summary>
    public static IOrderedQueryable<T> ThenBy<T>(IOrderedQueryable<T> source, string propertyPath)
        => AppendOrder(source, propertyPath, descending: false, isThenBy: true);

    /// <summary>Appends <c>ThenByDescending</c> on a path of the queried or a joined entity.</summary>
    public static IOrderedQueryable<T> ThenByDescending<T>(IOrderedQueryable<T> source, string propertyPath)
        => AppendOrder(source, propertyPath, descending: true, isThenBy: true);

    /// <summary>Appends a <c>Select</c> after one the generator reads through, which replaces it.</summary>
    /// <param name="source">The projected query.</param>
    /// <param name="propertyNames">The paths the reader reads, of the queried entity and of the joined ones.</param>
    /// <param name="entityPaths">The paths of the entities the reader uses.</param>
    /// <param name="reader">Builds each result.</param>
    public static IQueryable<TResult> Select<T, TResult>(
        IQueryable<T> source, IReadOnlyList<string> propertyNames, IReadOnlyList<string> entityPaths, ProjectionReader<TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);
        ArgumentNullException.ThrowIfNull(entityPaths);
        ArgumentNullException.ThrowIfNull(reader);
        if (propertyNames.Count == 0 && entityPaths.Count == 0)
        {
            throw new ArgumentException(ExceptionMessages.ProjectionRequiresColumns, nameof(propertyNames));
        }

        var query = ToProjected(source);
        return new ProjectedQueryable<TResult>(query.Context, new ProjectionNode(query.Tail, propertyNames, entityPaths), reader);
    }

    /// <summary>Appends <c>Join</c> after a projection the generator reads through, such as the pair of an earlier join.</summary>
    /// <exception cref="InvalidOperationException">The inner query belongs to another context.</exception>
    public static IQueryable<TResult> Join<TOuter, [DynamicallyAccessedMembers(EntityMembers.Required)] TInner, TResult>(
        IQueryable<TOuter> outer,
        IEnumerable<TInner> inner,
        IReadOnlyList<string> outerKeys,
        IReadOnlyList<string> innerKeys,
        IReadOnlyList<string> propertyNames,
        IReadOnlyList<string> entityPaths,
        ProjectionReader<TResult> reader)
        where TInner : class
    {
        var query = ToProjected(outer);
        return Infrastructure.SqlQuery.Joined(query.Context, query.Tail, inner, outerKeys, innerKeys, propertyNames, entityPaths, reader);
    }

    private static ProjectedQueryable<T> AppendOrder<T>(IQueryable<T> source, string propertyPath, bool descending, bool isThenBy)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyPath);

        var query = ToProjected(source);
        return query.WithTail(new OrderNode(query.Tail, propertyPath, descending, isThenBy));
    }

    private static ProjectedQueryable<T> AppendGroupOrder<T>(IQueryable<T> source, GroupValue value, object?[] values, bool descending, bool isThenBy)
    {
        var query = ToProjected(source);
        return query.WithTail(SqlGrouping.CreateOrder(query.Tail, value, values, descending, isThenBy));
    }

    internal static ProjectedQueryable<T> ToProjected<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source as ProjectedQueryable<T>
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NotAProjectedQuery, source.GetType()));
    }
}
