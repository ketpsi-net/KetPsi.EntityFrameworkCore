using System.Data.Common;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Infrastructure;
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
        DbContext context, ISqlDialect dialect, SqlStatement statement, IEntityReader<T> reader, int[] ordinals)
    {
        var database = context.Database;
        database.OpenConnection();
        try
        {
            using var command = CreateCommand(database, dialect, statement);
            using var dataReader = command.ExecuteReader();
            while (dataReader.Read())
            {
                yield return reader.Read(dataReader, ordinals);
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
        IEntityReader<T> reader,
        int[] ordinals,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = CreateCommand(database, dialect, statement);
            await using (command.ConfigureAwait(false))
            {
                var dataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (dataReader.ConfigureAwait(false))
                {
                    while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        yield return reader.Read(dataReader, ordinals);
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
            return command.ExecuteScalar();
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
                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Name;
            dbParameter.Value = ToProviderValue(parameter.Value);
            dialect.ConfigureParameter(dbParameter, parameter.Property?.StoreType);
            command.Parameters.Add(dbParameter);
        }

        return command;
    }

    // Enums without a value converter are stored as their underlying number, which providers expect as that type.
    internal static object ToProviderValue(object value)
        => value is Enum enumValue
            ? Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), System.Globalization.CultureInfo.InvariantCulture)
            : value;
}
