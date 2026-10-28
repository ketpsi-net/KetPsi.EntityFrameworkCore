using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Executes a query for a terminal operator: binds the model, resolves the dialect and query filters, builds
/// the statement and reads the result. Results and exceptions match LINQ and EF Core.
/// </summary>
internal static class QueryRunner
{
    private const int FirstRowLimit = 1;

    // Two rows are enough to tell "exactly one" from "more than one".
    private const int SingleRowLimit = 2;

    // EF Core 9 is the first version that can track entities under Native AOT, with a --nativeaot compiled model.
    private const int MinimumEfCoreMajorForAotTracking = 9;

    private static readonly int EfCoreMajorVersion = typeof(DbContext).Assembly.GetName().Version?.Major ?? 0;

    public static IEnumerable<T> Enumerate<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
    {
        var rows = PrepareRows(query, rowLimit: null);
        return QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize);
    }

    public static IAsyncEnumerable<T> EnumerateAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
    {
        var rows = PrepareRows(query, rowLimit: null);
        return QueryExecutor.StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Materialize, cancellationToken);
    }

    public static List<T> ToList<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => ReadRows(query, rowLimit: null);

    public static Task<List<T>> ToListAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => ReadRowsAsync(query, rowLimit: null, cancellationToken);

    public static T First<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => FirstOf(ReadRows(query, FirstRowLimit), isAsync: false);

    public static async Task<T> FirstAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => FirstOf(await ReadRowsAsync(query, FirstRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? FirstOrDefault<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => FirstOrDefaultOf(ReadRows(query, FirstRowLimit));

    public static async Task<T?> FirstOrDefaultAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => FirstOrDefaultOf(await ReadRowsAsync(query, FirstRowLimit, cancellationToken).ConfigureAwait(false));

    public static T Single<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => SingleOf(ReadRows(query, SingleRowLimit), isAsync: false);

    public static async Task<T> SingleAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => SingleOf(await ReadRowsAsync(query, SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? SingleOrDefault<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => SingleOrDefaultOf(ReadRows(query, SingleRowLimit), isAsync: false);

    public static async Task<T?> SingleOrDefaultAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => SingleOrDefaultOf(await ReadRowsAsync(query, SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static int Count<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => Convert.ToInt32(ExecuteScalar(query, QueryTerminal.Count), CultureInfo.InvariantCulture);

    public static async Task<int> CountAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => Convert.ToInt32(await ExecuteScalarAsync(query, QueryTerminal.Count, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static long LongCount<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => Convert.ToInt64(ExecuteScalar(query, QueryTerminal.LongCount), CultureInfo.InvariantCulture);

    public static async Task<long> LongCountAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => Convert.ToInt64(await ExecuteScalarAsync(query, QueryTerminal.LongCount, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static bool Any<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => Convert.ToBoolean(ExecuteScalar(query, QueryTerminal.Any), CultureInfo.InvariantCulture);

    public static async Task<bool> AnyAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => Convert.ToBoolean(await ExecuteScalarAsync(query, QueryTerminal.Any, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>The tracking behavior of a query: the last <c>AsTracking</c> or <c>AsNoTracking</c>, else the context default.</summary>
    internal static QueryTrackingBehavior ResolveTracking(QueryNode tail, QueryTrackingBehavior contextDefault)
    {
        for (var node = tail; node is not null; node = node.Parent)
        {
            if (node is TrackingNode tracking)
            {
                return tracking.Behavior;
            }
        }

        return contextDefault;
    }

    /// <summary>Throws when entities would be tracked where EF Core cannot track them: under Native AOT before EF Core 9.</summary>
    internal static void EnsureTrackingSupported(bool isDynamicCodeSupported, int efCoreMajorVersion)
    {
        if (!isDynamicCodeSupported && efCoreMajorVersion < MinimumEfCoreMajorForAotTracking)
        {
            throw new InvalidOperationException(ExceptionMessages.TrackingRequiresEfCore9UnderAot);
        }
    }

    private static List<T> ReadRows<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, int? rowLimit)
        where T : class
    {
        var rows = PrepareRows(query, rowLimit);
        return [.. QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize)];
    }

    private static async Task<List<T>> ReadRowsAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, int? rowLimit, CancellationToken cancellationToken)
        where T : class
    {
        var rows = PrepareRows(query, rowLimit);
        var result = new List<T>();
        await foreach (var row in QueryExecutor
            .StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Materialize, cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(row);
        }

        return result;
    }

    private static object? ExecuteScalar<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, QueryTerminal terminal)
        where T : class
    {
        var (dialect, statement) = Prepare(query, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalar(query.Context, dialect, statement);
    }

    private static Task<object?> ExecuteScalarAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, QueryTerminal terminal, CancellationToken cancellationToken)
        where T : class
    {
        var (dialect, statement) = Prepare(query, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalarAsync(query.Context, dialect, statement, cancellationToken);
    }

    private static PreparedRows<T> PrepareRows<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, int? rowLimit)
        where T : class
    {
        var reader = EntityReaders.Get<T>();
        var (entity, dialect, statement) = PrepareWithEntity(query, QueryTerminal.Rows, rowLimit);
        var ordinals = entity.GetOrdinals(reader.PropertyNames);
        Func<DbDataReader, T> materialize = row => reader.Read(row, ordinals);

        // Keyless entity types are never tracked or identity-resolved, as in EF Core.
        var tracking = ResolveTracking(query.Tail, query.Context.ChangeTracker.QueryTrackingBehavior);
        var untracked = materialize;
        if (tracking == QueryTrackingBehavior.TrackAll && entity.PrimaryKey is not null)
        {
            EnsureTrackingSupported(RuntimeFeature.IsDynamicCodeSupported, EfCoreMajorVersion);
            var tracker = new EntityTracker<T>(query.Context, entity);
            materialize = row => tracker.Track(row, untracked);
        }
        else if (tracking == QueryTrackingBehavior.NoTrackingWithIdentityResolution && entity.PrimaryKey is not null)
        {
            // Prepared once per execution, so each execution resolves identities on its own.
            var identities = new IdentityMap<T>(entity);
            materialize = row => identities.Resolve(row, untracked);
        }

        return new PreparedRows<T>(dialect, statement, materialize);
    }

    private static (ISqlDialect Dialect, SqlStatement Statement) Prepare<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, QueryTerminal terminal, int? rowLimit)
        where T : class
    {
        var (_, dialect, statement) = PrepareWithEntity(query, terminal, rowLimit);
        return (dialect, statement);
    }

    private static (BoundEntity Entity, ISqlDialect Dialect, SqlStatement Statement) PrepareWithEntity<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, QueryTerminal terminal, int? rowLimit)
        where T : class
    {
        var context = query.Context;
        var entity = ModelBinder.GetEntity(context.Model, query.Root.EntityType);
        var dialect = SqlDialects.Resolve(context.Database.ProviderName);
        var filters = QueryFilterResolver.Resolve(context, entity, IgnoredQueryFilters.FromChain(query.Tail));
        var statement = SqlBuilder.Build(entity, query.Tail, dialect, terminal, rowLimit, filters);
        return (entity, dialect, statement);
    }

    // LINQ's messages for sync terminals and EF Core's for async ones, so callers see the exceptions EF Core throws.
    private static T FirstOf<T>(List<T> rows, bool isAsync) => rows.Count > 0 ? rows[0] : throw NoElements(isAsync);

    private static T? FirstOrDefaultOf<T>(List<T> rows) => rows.Count > 0 ? rows[0] : default;

    private static T SingleOf<T>(List<T> rows, bool isAsync) => rows.Count switch
    {
        0 => throw NoElements(isAsync),
        1 => rows[0],
        _ => throw MoreThanOneElement(isAsync),
    };

    private static T? SingleOrDefaultOf<T>(List<T> rows, bool isAsync) => rows.Count switch
    {
        0 => default,
        1 => rows[0],
        _ => throw MoreThanOneElement(isAsync),
    };

    private static InvalidOperationException NoElements(bool isAsync)
        => new(isAsync ? ExceptionMessages.SequenceContainsNoElementsAsync : ExceptionMessages.SequenceContainsNoElements);

    private static InvalidOperationException MoreThanOneElement(bool isAsync)
        => new(isAsync ? ExceptionMessages.SequenceContainsMoreThanOneElementAsync : ExceptionMessages.SequenceContainsMoreThanOneElement);

    private sealed record PreparedRows<T>(ISqlDialect Dialect, SqlStatement Statement, Func<DbDataReader, T> Materialize);
}
