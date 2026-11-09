using System.Data.Common;
using System.Diagnostics;
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
    internal const int FirstRowLimit = 1;

    // Two rows are enough to tell "exactly one" from "more than one".
    internal const int SingleRowLimit = 2;

    // EF Core names the synchronous operator in its message for both forms, as these do.
    internal const string LastMethod = "Last";

    internal const string LastOrDefaultMethod = "LastOrDefault";

    // EF Core 9 is the first version that can track entities under Native AOT, with a --nativeaot compiled model.
    private const int MinimumEfCoreMajorForAotTracking = 9;

    internal static readonly int EfCoreMajorVersion = typeof(DbContext).Assembly.GetName().Version?.Major ?? 0;

    public static IEnumerable<T> Enumerate<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
    {
        if (PrepareIncludes(query, rowLimit: null) is { } included)
        {
            return LoadIncluded<T>(included);
        }

        var rows = PrepareRows(query, rowLimit: null);
        return QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize);
    }

    public static IAsyncEnumerable<T> EnumerateAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
    {
        if (PrepareIncludes(query, rowLimit: null) is { } included)
        {
            return LoadIncludedAsync<T>(included, cancellationToken);
        }

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

    public static T Last<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => FirstOf(ReadRows(Reversed(query, LastMethod), FirstRowLimit), isAsync: false);

    public static async Task<T> LastAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => FirstOf(await ReadRowsAsync(Reversed(query, LastMethod), FirstRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? LastOrDefault<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query)
        where T : class
        => FirstOrDefaultOf(ReadRows(Reversed(query, LastOrDefaultMethod), FirstRowLimit));

    public static async Task<T?> LastOrDefaultAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, CancellationToken cancellationToken)
        where T : class
        => FirstOrDefaultOf(await ReadRowsAsync(Reversed(query, LastOrDefaultMethod), FirstRowLimit, cancellationToken).ConfigureAwait(false));

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

    // Last is the first row of the query read in reverse.
    private static SqlQueryable<T> Reversed<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, string method)
        where T : class
        => query.WithTail(new ReverseOrderNode(query.Tail, method));

    private static List<T> ReadRows<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, int? rowLimit)
        where T : class
    {
        if (PrepareIncludes(query, rowLimit) is { } included)
        {
            return included.Loader.Load(included.Statement).ConvertAll(static entity => (T)entity);
        }

        var rows = PrepareRows(query, rowLimit);
        return [.. QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize)];
    }

    private static async Task<List<T>> ReadRowsAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        SqlQueryable<T> query, int? rowLimit, CancellationToken cancellationToken)
        where T : class
    {
        if (PrepareIncludes(query, rowLimit) is { } included)
        {
            var entities = await included.Loader.LoadAsync(included.Statement, cancellationToken).ConfigureAwait(false);
            return entities.ConvertAll(static entity => (T)entity);
        }

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

    // Collections load with the keys of every parent, so the parents are read in full before any is returned.
    private static IEnumerable<T> LoadIncluded<T>(IncludedQuery included)
    {
        foreach (var entity in included.Loader.Load(included.Statement))
        {
            yield return (T)entity;
        }
    }

    private static async IAsyncEnumerable<T> LoadIncludedAsync<T>(
        IncludedQuery included, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var entity in await included.Loader.LoadAsync(included.Statement, cancellationToken).ConfigureAwait(false))
        {
            yield return (T)entity;
        }
    }

    /// <summary>The statement and loader for a query with included navigations, or <see langword="null"/> without.</summary>
    private static IncludedQuery? PrepareIncludes<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(SqlQueryable<T> query, int? rowLimit)
        where T : class
    {
        var context = query.Context;
        var bind = Binder(context);
        var entity = bind(query.Root.EntityType);
        if (IncludeTree.FromChain(entity, query.Tail, bind) is not { } includes)
        {
            return null;
        }

        var dialect = SqlDialects.Resolve(context.Database.ProviderName);
        var filtersFor = FiltersFor(context, query.Tail);
        var tracking = ResolveTracking(query.Tail, context.ChangeTracker.QueryTrackingBehavior);
        if (tracking == QueryTrackingBehavior.TrackAll)
        {
            EnsureTrackingSupported(RuntimeFeature.IsDynamicCodeSupported, EfCoreMajorVersion);
        }
        else if (tracking == QueryTrackingBehavior.NoTracking)
        {
            includes.EnsureNoCycle();
        }

        var statement = SqlBuilder.BuildRows(entity, query.Tail, dialect, includes, filtersFor, rowLimit, bind);
        return new IncludedQuery(statement, new IncludeLoader(context, dialect, tracking, filtersFor, bind));
    }

    /// <summary>Binds entity types against the context's model.</summary>
    internal static Func<Type, BoundEntity> Binder(DbContext context)
    {
        var model = context.Model;
        return type => ModelBinder.GetEntity(model, type);
    }

    /// <summary>
    /// Resolves the query filters of each entity a query reads, once per entity. <c>IgnoreQueryFilters</c> applies to
    /// every entity of the query, joined and included ones too, as in EF Core.
    /// </summary>
    internal static Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> FiltersFor(DbContext context, QueryNode tail)
    {
        var ignored = IgnoredQueryFilters.FromChain(tail);
        var filters = new Dictionary<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>(ReferenceEqualityComparer.Instance);
        return bound =>
        {
            if (!filters.TryGetValue(bound, out var resolved))
            {
                filters.Add(bound, resolved = QueryFilterResolver.Resolve(context, bound, ignored));
            }

            return resolved;
        };
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
        => PrepareStatement(query.Context, query.Tail, terminal, rowLimit);

    /// <summary>Binds the chain's root entity, resolves the dialect and query filters, and builds the statement.</summary>
    internal static (BoundEntity Entity, ISqlDialect Dialect, SqlStatement Statement) PrepareStatement(
        DbContext context, QueryNode tail, QueryTerminal terminal, int? rowLimit, SqlAggregate? aggregate = null)
    {
        var bind = Binder(context);
        var entity = bind(RootOf(tail).EntityType);
        var dialect = SqlDialects.Resolve(context.Database.ProviderName);
        var filtersFor = FiltersFor(context, tail);
        var statement = SqlBuilder.Build(entity, tail, dialect, terminal, rowLimit, filtersFor(entity), aggregate, filtersFor, bind);
        return (entity, dialect, statement);
    }

    /// <summary>Executes a scalar terminal, such as <c>Count</c> or <c>Any</c>, over the chain ending at <paramref name="tail"/>.</summary>
    internal static object? ExecuteScalar(DbContext context, QueryNode tail, QueryTerminal terminal)
    {
        var (_, dialect, statement) = PrepareStatement(context, tail, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalar(context, dialect, statement);
    }

    /// <summary>Executes the async form of <see cref="ExecuteScalar(DbContext, QueryNode, QueryTerminal)"/>.</summary>
    internal static Task<object?> ExecuteScalarAsync(DbContext context, QueryNode tail, QueryTerminal terminal, CancellationToken cancellationToken)
    {
        var (_, dialect, statement) = PrepareStatement(context, tail, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalarAsync(context, dialect, statement, cancellationToken);
    }

    internal static RootNode RootOf(QueryNode tail)
    {
        var node = tail;
        while (node.Parent is not null)
        {
            node = node.Parent;
        }

        return node as RootNode ?? throw new UnreachableException("A chain always starts with a root node.");
    }

    // LINQ's messages for sync terminals and EF Core's for async ones, so callers see the exceptions EF Core throws.
    internal static T FirstOf<T>(List<T> rows, bool isAsync) => rows.Count > 0 ? rows[0] : throw NoElements(isAsync);

    internal static T? FirstOrDefaultOf<T>(List<T> rows) => rows.Count > 0 ? rows[0] : default;

    internal static T SingleOf<T>(List<T> rows, bool isAsync) => rows.Count switch
    {
        0 => throw NoElements(isAsync),
        1 => rows[0],
        _ => throw MoreThanOneElement(isAsync),
    };

    internal static T? SingleOrDefaultOf<T>(List<T> rows, bool isAsync) => rows.Count switch
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

    private sealed record IncludedQuery(IncludeStatement Statement, IncludeLoader Loader);
}
