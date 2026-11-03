using System.Data.Common;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Executes projected queries: the statement selects the projected columns, and the generated reader builds each
/// result. Projections of entity properties are never tracked. Results and exceptions match LINQ and EF Core.
/// </summary>
internal static class ProjectionRunner
{
    public static IEnumerable<T> Enumerate<T>(ProjectedQueryable<T> query)
    {
        var rows = Prepare(query, rowLimit: null);
        return QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize);
    }

    public static IAsyncEnumerable<T> EnumerateAsync<T>(ProjectedQueryable<T> query, CancellationToken cancellationToken)
    {
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
        var rows = Prepare(query, rowLimit);
        return [.. QueryExecutor.Stream(query.Context, rows.Dialect, rows.Statement, rows.Materialize)];
    }

    private static async Task<List<T>> ReadRowsAsync<T>(ProjectedQueryable<T> query, int? rowLimit, CancellationToken cancellationToken)
    {
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
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(query.Context, query.Tail, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalar(query.Context, dialect, statement);
    }

    private static Task<object?> ExecuteScalarAsync<T>(ProjectedQueryable<T> query, QueryTerminal terminal, CancellationToken cancellationToken)
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(query.Context, query.Tail, terminal, rowLimit: null);
        return QueryExecutor.ExecuteScalarAsync(query.Context, dialect, statement, cancellationToken);
    }

    private static PreparedRows<T> Prepare<T>(ProjectedQueryable<T> query, int? rowLimit)
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(query.Context, query.Tail, QueryTerminal.Rows, rowLimit);
        var ordinals = Ordinals(query.Projection);
        var reader = query.Reader;
        return new PreparedRows<T>(dialect, statement, row => reader(row, ordinals));
    }

    private sealed record PreparedRows<T>(ISqlDialect Dialect, SqlStatement Statement, Func<DbDataReader, T> Materialize);
}
