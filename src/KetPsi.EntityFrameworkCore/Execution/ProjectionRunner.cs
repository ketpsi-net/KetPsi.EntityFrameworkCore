using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Executes projected queries: the statement selects the projected columns and entities, and the generated reader
/// builds each result. Projected entities are tracked, identity-resolved and given their includes as in EF Core, so
/// a projection with entities reads every row before it returns any; one with only columns streams them.
/// Results and exceptions match LINQ and EF Core.
/// </summary>
internal static class ProjectionRunner
{
    public static IEnumerable<T> Enumerate<T>(ProjectedQueryable<T> query)
    {
        if (PrepareEntities(query, rowLimit: null) is { } entities)
        {
            return LoadEntities(entities);
        }

        var rows = Prepare(query, rowLimit: null);
        return QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize);
    }

    public static IAsyncEnumerable<T> EnumerateAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
    {
        if (PrepareEntities(query, rowLimit: null) is { } entities)
        {
            return LoadEntitiesAsync(entities, cancellationToken);
        }

        var rows = Prepare(query, rowLimit: null);
        return QueryExecutor.StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Materialize, cancellationToken);
    }

    public static List<T> ToList<T>(ProjectedQueryable<T> query) => ReadRows(query, rowLimit: null);

    public static Task<List<T>> ToListAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => ReadRowsAsync(query, rowLimit: null, cancellationToken);

    public static T First<T>(ProjectedQueryable<T> query)
        => QueryRunner.FirstOf(ReadRows(query, QueryRunner.FirstRowLimit), isAsync: false);

    public static async Task<T> FirstAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => QueryRunner.FirstOf(await ReadRowsAsync(query, QueryRunner.FirstRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? FirstOrDefault<T>(ProjectedQueryable<T> query)
        => QueryRunner.FirstOrDefaultOf(ReadRows(query, QueryRunner.FirstRowLimit));

    public static async Task<T?> FirstOrDefaultAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => QueryRunner.FirstOrDefaultOf(await ReadRowsAsync(query, QueryRunner.FirstRowLimit, cancellationToken).ConfigureAwait(false));

    public static T Single<T>(ProjectedQueryable<T> query)
        => QueryRunner.SingleOf(ReadRows(query, QueryRunner.SingleRowLimit), isAsync: false);

    public static async Task<T> SingleAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => QueryRunner.SingleOf(await ReadRowsAsync(query, QueryRunner.SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? SingleOrDefault<T>(ProjectedQueryable<T> query)
        => QueryRunner.SingleOrDefaultOf(ReadRows(query, QueryRunner.SingleRowLimit), isAsync: false);

    public static async Task<T?> SingleOrDefaultAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => QueryRunner.SingleOrDefaultOf(await ReadRowsAsync(query, QueryRunner.SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static int Count<T>(ProjectedQueryable<T> query)
        => Convert.ToInt32(ExecuteScalar(query, QueryTerminal.Count), CultureInfo.InvariantCulture);

    public static async Task<int> CountAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToInt32(await ExecuteScalarAsync(query, QueryTerminal.Count, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static long LongCount<T>(ProjectedQueryable<T> query)
        => Convert.ToInt64(ExecuteScalar(query, QueryTerminal.LongCount), CultureInfo.InvariantCulture);

    public static async Task<long> LongCountAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToInt64(await ExecuteScalarAsync(query, QueryTerminal.LongCount, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static bool Any<T>(ProjectedQueryable<T> query)
        => Convert.ToBoolean(ExecuteScalar(query, QueryTerminal.Any), CultureInfo.InvariantCulture);

    public static async Task<bool> AnyAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToBoolean(await ExecuteScalarAsync(query, QueryTerminal.Any, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>
    /// For each property name the reader was created with, its ordinal in the statement, which selects
    /// <see cref="ProjectionNode.Columns"/> in order.
    /// </summary>
    internal static int[] Ordinals(ProjectionNode projection)
    {
        var columns = projection.Columns;
        var ordinals = new int[projection.PropertyNames.Count];
        for (var i = 0; i < ordinals.Length; i++)
        {
            ordinals[i] = IndexOf(columns, projection.PropertyNames[i]);
        }

        return ordinals;
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(name, nameof(name));
    }

    private static List<T> ReadRows<T>(ProjectedQueryable<T> query, int? rowLimit)
    {
        if (PrepareEntities(query, rowLimit) is { } entities)
        {
            return entities.Loader.Load(entities.Statement, entities.Read);
        }

        var rows = Prepare(query, rowLimit);
        return [.. QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize)];
    }

    private static async Task<List<T>> ReadRowsAsync<T>(ProjectedQueryable<T> query, int? rowLimit, CancellationToken cancellationToken)
    {
        if (PrepareEntities(query, rowLimit) is { } entities)
        {
            return await entities.Loader.LoadAsync(entities.Statement, entities.Read, cancellationToken).ConfigureAwait(false);
        }

        var rows = Prepare(query, rowLimit);
        var result = new List<T>();
        await foreach (var row in QueryExecutor
            .StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Materialize, cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(row);
        }

        return result;
    }

    private static object? ExecuteScalar<T>(ProjectedQueryable<T> query, QueryTerminal terminal)
        => QueryRunner.ExecuteScalar(query.Context, query.Tail, terminal);

    private static Task<object?> ExecuteScalarAsync<T>(ProjectedQueryable<T> query, QueryTerminal terminal, CancellationToken cancellationToken)
        => QueryRunner.ExecuteScalarAsync(query.Context, query.Tail, terminal, cancellationToken);

    private static PreparedRows<T> Prepare<T>(ProjectedQueryable<T> query, int? rowLimit)
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(query.Context, query.Tail, QueryTerminal.Rows, rowLimit);
        var ordinals = Ordinals(query.Projection);
        var reader = query.Reader;
        return new PreparedRows<T>(dialect, statement, row => reader(row, ordinals, []));
    }

    /// <summary>
    /// For each value of a grouped <c>Select</c>, its ordinal in the statement, which selects the values in order.
    /// </summary>
    internal static int[] Ordinals(GroupSelectNode projection) => [.. Enumerable.Range(0, projection.Values.Count)];

    private static int[] Ordinals(QueryNode projection) => projection switch
    {
        ProjectionNode entityProjection => Ordinals(entityProjection),
        GroupSelectNode groupProjection => Ordinals(groupProjection),
        _ => throw new UnreachableException(projection.GetType().ToString()),
    };

    // Included collections load with the keys of every entity read, so every row is read before any is returned.
    private static IEnumerable<T> LoadEntities<T>(ProjectedEntities<T> entities)
    {
        foreach (var result in entities.Loader.Load(entities.Statement, entities.Read))
        {
            yield return result;
        }
    }

    private static async IAsyncEnumerable<T> LoadEntitiesAsync<T>(
        ProjectedEntities<T> entities, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var result in await entities.Loader.LoadAsync(entities.Statement, entities.Read, cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }
    }

    /// <summary>The statement and loader for a projection that selects entities, or <see langword="null"/> without.</summary>
    private static ProjectedEntities<T>? PrepareEntities<T>(ProjectedQueryable<T> query, int? rowLimit)
    {
        // Groups have no entities: their values are keys and aggregates.
        if (query.Projection is not ProjectionNode { EntityPaths.Count: > 0 } projection)
        {
            return null;
        }

        var context = query.Context;
        var bind = QueryRunner.Binder(context);
        var entity = bind(QueryRunner.RootOf(query.Tail).EntityType);
        var includes = IncludeTree.FromChain(entity, query.Tail, bind);
        var dialect = SqlDialects.Resolve(context.Database.ProviderName);
        var filtersFor = QueryRunner.FiltersFor(context, query.Tail);

        var tracking = QueryRunner.ResolveTracking(query.Tail, context.ChangeTracker.QueryTrackingBehavior);
        if (tracking == QueryTrackingBehavior.TrackAll)
        {
            QueryRunner.EnsureTrackingSupported(RuntimeFeature.IsDynamicCodeSupported, QueryRunner.EfCoreMajorVersion);
        }
        else if (tracking == QueryTrackingBehavior.NoTracking)
        {
            // Only the includes a projected entity gets are applied, so only they can form a cycle, as in EF Core.
            foreach (var path in projection.EntityPaths)
            {
                includes?.Find(path)?.EnsureNoCycle();
            }
        }

        var statement = SqlBuilder.BuildRows(entity, query.Tail, dialect, includes, filtersFor, rowLimit, bind);
        var ordinals = Ordinals(projection);
        var reader = query.Reader;
        return new ProjectedEntities<T>(
            statement,
            new IncludeLoader(context, dialect, tracking, filtersFor, bind),
            (row, read) => reader(row, ordinals, read));
    }

    private sealed record PreparedRows<T>(ISqlDialect Dialect, SqlStatement Statement, Func<DbDataReader, T> Materialize);

    private sealed record ProjectedEntities<T>(IncludeStatement Statement, IncludeLoader Loader, Func<DbDataReader, object?[], T> Read);
}
