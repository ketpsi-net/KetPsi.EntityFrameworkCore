using System.Data.Common;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Runs statements on the context's own connection. Opening and closing go through EF Core, so a connection the
/// application opened stays open, and commands join the context's current transaction.
/// </summary>
internal static class QueryExecutor
{
    public static IEnumerable<T> Stream<T>(
        DbContext context, ISqlDialect dialect, SqlStatement statement, Func<DbDataReader, T> materialize)
    {
        var database = context.Database;
        database.OpenConnection();
        try
        {
            using var command = CreateCommand(database, dialect, statement);
            using var dataReader = Columns(command.ExecuteReader(), statement);
            while (dataReader.Read())
            {
                yield return materialize(dataReader);
            }
        }
        finally
        {
            database.CloseConnection();
        }
    }

    public static async IAsyncEnumerable<T> StreamAsync<T>(
        DbContext context,
        ISqlDialect dialect,
        SqlStatement statement,
        Func<DbDataReader, T> materialize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = CreateCommand(database, dialect, statement);
            await using (command.ConfigureAwait(false))
            {
                var dataReader = Columns(await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false), statement);
                await using (dataReader.ConfigureAwait(false))
                {
                    while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        yield return materialize(dataReader);
                    }
                }
            }
        }
        finally
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public static object? ExecuteScalar(DbContext context, ISqlDialect dialect, SqlStatement statement)
    {
        var database = context.Database;
        database.OpenConnection();
        try
        {
            using var command = CreateCommand(database, dialect, statement);
            return Scalar(command.ExecuteScalar(), statement);
        }
        finally
        {
            database.CloseConnection();
        }
    }

    public static async Task<object?> ExecuteScalarAsync(
        DbContext context, ISqlDialect dialect, SqlStatement statement, CancellationToken cancellationToken)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = CreateCommand(database, dialect, statement);
            await using (command.ConfigureAwait(false))
            {
                return Scalar(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), statement);
            }
        }
        finally
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Executes a statement that changes rows, such as <c>ExecuteDelete</c>, and returns how many it changed.</summary>
    public static int ExecuteNonQuery(DbContext context, ISqlDialect dialect, SqlStatement statement)
    {
        var database = context.Database;
        database.OpenConnection();
        try
        {
            using var command = CreateCommand(database, dialect, statement);
            return command.ExecuteNonQuery();
        }
        finally
        {
            database.CloseConnection();
        }
    }

    /// <summary>Executes the async form of <see cref="ExecuteNonQuery"/>.</summary>
    public static async Task<int> ExecuteNonQueryAsync(
        DbContext context, ISqlDialect dialect, SqlStatement statement, CancellationToken cancellationToken)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = CreateCommand(database, dialect, statement);
            await using (command.ConfigureAwait(false))
            {
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    internal static DbCommand CreateCommand(
        Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database, ISqlDialect dialect, SqlStatement statement)
    {
        var command = database.GetDbConnection().CreateCommand();
        command.CommandText = statement.CommandText;
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();
        if (database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        foreach (var parameter in statement.Parameters)
        {
            // A DbParameter given to FromSql is sent as it is, as EF Core sends it.
            if (parameter.Value is DbParameter given)
            {
                command.Parameters.Add(given);
                continue;
            }

            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Name;
            dbParameter.Value = ToProviderValue(parameter.Value);
            dialect.ConfigureParameter(dbParameter, parameter.Property?.StoreType);
            command.Parameters.Add(dbParameter);
        }

        return command;
    }

    // SQL of FromSql sent as it is returns its columns in its own order, so they are read by name. Columns of properties
    // with a value converter are read as their CLR values.
    private static DbDataReader Columns(DbDataReader reader, SqlStatement statement)
    {
        var columns = statement.ColumnsByName is { } names ? new ColumnMappedReader(reader, names) : reader;
        return statement.ConvertedColumns is { } converted ? new ConvertingReader(columns, converted) : columns;
    }

    // Min or Max of a converted column returns its stored value, which the converter reads, as in EF Core.
    private static object? Scalar(object? value, SqlStatement statement)
        => statement.ConvertedColumns is [{ } property] && value is not (null or DBNull) ? property.FromProvider(value) : value;

    // Enums without a value converter are stored as their underlying number, which providers expect as that type.
    internal static object ToProviderValue(object value)
        => value is Enum enumValue
            ? Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), System.Globalization.CultureInfo.InvariantCulture)
            : value;
}
