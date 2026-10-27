using System.Globalization;
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

    public static IEnumerable<T> Enumerate<T>(SqlQueryable<T> query)
    {
        var rows = PrepareRows(query, rowLimit: null);
        return QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Reader, rows.Ordinals);
    }

    public static IAsyncEnumerable<T> EnumerateAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
    {
        var rows = PrepareRows(query, rowLimit: null);
        return QueryExecutor.StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Reader, rows.Ordinals, cancellationToken);
    }

    public static List<T> ToList<T>(SqlQueryable<T> query) => ReadRows(query, rowLimit: null);

    public static Task<List<T>> ToListAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => ReadRowsAsync(query, rowLimit: null, cancellationToken);

    public static T First<T>(SqlQueryable<T> query)
        => FirstOf(ReadRows(query, FirstRowLimit), isAsync: false);

    public static async Task<T> FirstAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => FirstOf(await ReadRowsAsync(query, FirstRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? FirstOrDefault<T>(SqlQueryable<T> query)
        => FirstOrDefaultOf(ReadRows(query, FirstRowLimit));

    public static async Task<T?> FirstOrDefaultAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => FirstOrDefaultOf(await ReadRowsAsync(query, FirstRowLimit, cancellationToken).ConfigureAwait(false));

    public static T Single<T>(SqlQueryable<T> query)
        => SingleOf(ReadRows(query, SingleRowLimit), isAsync: false);

    public static async Task<T> SingleAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => SingleOf(await ReadRowsAsync(query, SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static T? SingleOrDefault<T>(SqlQueryable<T> query)
        => SingleOrDefaultOf(ReadRows(query, SingleRowLimit), isAsync: false);

    public static async Task<T?> SingleOrDefaultAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => SingleOrDefaultOf(await ReadRowsAsync(query, SingleRowLimit, cancellationToken).ConfigureAwait(false), isAsync: true);

    public static int Count<T>(SqlQueryable<T> query)
        => Convert.ToInt32(ExecuteScalar(query, QueryTerminal.Count), CultureInfo.InvariantCulture);

    public static async Task<int> CountAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToInt32(await ExecuteScalarAsync(query, QueryTerminal.Count, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static long LongCount<T>(SqlQueryable<T> query)
        => Convert.ToInt64(ExecuteScalar(query, QueryTerminal.LongCount), CultureInfo.InvariantCulture);

    public static async Task<long> LongCountAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToInt64(await ExecuteScalarAsync(query, QueryTerminal.LongCount, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    public static bool Any<T>(SqlQueryable<T> query)
        => Convert.ToBoolean(ExecuteScalar(query, QueryTerminal.Any), CultureInfo.InvariantCulture);

    public static async Task<bool> AnyAsync<T>(SqlQueryable<T> query, CancellationToken cancellationToken)
        => Convert.ToBoolean(await ExecuteScalarAsync(query, QueryTerminal.Any, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

    private static List<T> ReadRows<T>(SqlQueryable<T> query, int? rowLimit)
    {
        var rows = PrepareRows(query, rowLimit);
        return [.. QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Reader, rows.Ordinals)];
    }

    private static async Task<List<T>> ReadRowsAsync<T>(SqlQueryable<T> query, int? rowLimit, CancellationToken cancellationToken)
    {
        var rows = PrepareRows(query, rowLimit);
        var result = new List<T>();
        await foreach (var row in QueryExecutor
            .StreamAsync(query.Context, rows.Dialect, rows.Statement, rows.Reader, rows.Ordinals, cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(row);
        }

        return result;
    }

    private static object? ExecuteScalar<T>(SqlQueryable<T> query, QueryTerminal terminal)
    {
        var (dialect, statement) = Prepare(query, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalar(query.Context, dialect, statement);
    }

    private static Task<object?> ExecuteScalarAsync<T>(SqlQueryable<T> query, QueryTerminal terminal, CancellationToken cancellationToken)
    {
        var (dialect, statement) = Prepare(query, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalarAsync(query.Context, dialect, statement, cancellationToken);
    }

    private static PreparedRows<T> PrepareRows<T>(SqlQueryable<T> query, int? rowLimit)
    {
        EnsureNoTracking(query);

        var reader = EntityReaders.Get<T>();
        var (entity, dialect, statement) = PrepareWithEntity(query, QueryTerminal.Rows, rowLimit);
        return new PreparedRows<T>(dialect, statement, reader, entity.GetOrdinals(reader.PropertyNames));
    }

    private static (ISqlDialect Dialect, SqlStatement Statement) Prepare<T>(SqlQueryable<T> query, QueryTerminal terminal, int? rowLimit)
    {
        var (_, dialect, statement) = PrepareWithEntity(query, terminal, rowLimit);
        return (dialect, statement);
    }

    private static (BoundEntity Entity, ISqlDialect Dialect, SqlStatement Statement) PrepareWithEntity<T>(
        SqlQueryable<T> query, QueryTerminal terminal, int? rowLimit)
    {
        var context = query.Context;
        var entity = ModelBinder.GetEntity(context.Model, query.Root.EntityType);
        var dialect = SqlDialects.Resolve(context.Database.ProviderName);
        var filters = QueryFilterResolver.Resolve(context, entity, IgnoredQueryFilters.FromChain(query.Tail));
        var statement = SqlBuilder.Build(entity, query.Tail, dialect, terminal, rowLimit, filters);
        return (entity, dialect, statement);
    }

    private static void EnsureNoTracking<T>(SqlQueryable<T> query)
    {
        if (query.Context.ChangeTracker.QueryTrackingBehavior != QueryTrackingBehavior.TrackAll)
        {
            return;
        }

        for (var node = query.Tail; node is not null; node = node.Parent)
        {
            if (node is NoTrackingNode)
            {
                return;
            }
        }

        throw new InvalidOperationException(ExceptionMessages.TrackingQueryNotSupported);
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

    private sealed record PreparedRows<T>(ISqlDialect Dialect, SqlStatement Statement, IEntityReader<T> Reader, int[] Ordinals);
}
