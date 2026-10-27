using System.Data;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class QueryExecutorTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public QueryExecutorTests()
    {
        _db = new FakeItemContext(_connection);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void CreateCommand_SetsTextAndConfiguredParameters()
    {
        var name = Property(nameof(Item.Name));
        var statement = new SqlStatement(
            "SELECT 1",
            [new QueryParameter("@p0", "abc", name), new QueryParameter("@p1", 10, Property: null)]);

        using var command = QueryExecutor.CreateCommand(_db.Database, SqlServerDialect.Instance, statement);

        Assert.Equal("SELECT 1", command.CommandText);
        var parameters = ((FakeDbCommand)command).Parameters;
        Assert.Equal(2, parameters.Count);
        Assert.Equal("@p0", parameters[0].ParameterName);
        Assert.Equal("abc", parameters[0].Value);
        Assert.Equal(DbType.String, parameters[0].DbType);
        Assert.Equal(-1, parameters[0].Size);
        Assert.Equal(10, parameters[1].Value);
    }

    [Fact]
    public void CreateCommand_JoinsCurrentTransaction()
    {
        _db.Database.OpenConnection();
        using var transaction = _db.Database.BeginTransaction();

        using var command = QueryExecutor.CreateCommand(_db.Database, SqlServerDialect.Instance, new SqlStatement("SELECT 1", []));

        Assert.Same(transaction.GetDbTransaction(), ((FakeDbCommand)command).AssignedTransaction);
    }

    [Fact]
    public void CreateCommand_WithoutTransaction_HasNone()
    {
        using var command = QueryExecutor.CreateCommand(_db.Database, SqlServerDialect.Instance, new SqlStatement("SELECT 1", []));

        Assert.Null(((FakeDbCommand)command).AssignedTransaction);
    }

    [Fact]
    public void CreateCommand_UsesContextCommandTimeout()
    {
        _db.Database.SetCommandTimeout(42);

        using var command = QueryExecutor.CreateCommand(_db.Database, SqlServerDialect.Instance, new SqlStatement("SELECT 1", []));

        Assert.Equal(42, command.CommandTimeout);
    }

    [Fact]
    public void ExecuteScalar_OpensAndClosesConnection()
    {
        _connection.Scalar = 5;

        var result = QueryExecutor.ExecuteScalar(_db, SqlServerDialect.Instance, new SqlStatement("SELECT 5", []));

        Assert.Equal(5, result);
        Assert.Equal(1, _connection.OpenCount);
        Assert.Equal(ConnectionState.Closed, _connection.State);
        Assert.True(_connection.LastCommand.IsDisposed);
    }

    [Fact]
    public async Task ExecuteScalarAsync_OpensAndClosesConnection()
    {
        _connection.Scalar = 5;

        var result = await QueryExecutor.ExecuteScalarAsync(
            _db, SqlServerDialect.Instance, new SqlStatement("SELECT 5", []), TestContext.Current.CancellationToken);

        Assert.Equal(5, result);
        Assert.Equal(ConnectionState.Closed, _connection.State);
        Assert.True(_connection.LastCommand.IsDisposed);
    }

    [Fact]
    public void ExecuteScalar_ConnectionOpenedByApplication_StaysOpen()
    {
        _db.Database.OpenConnection();

        QueryExecutor.ExecuteScalar(_db, SqlServerDialect.Instance, new SqlStatement("SELECT 1", []));

        Assert.Equal(ConnectionState.Open, _connection.State);
        Assert.Equal(1, _connection.OpenCount);
    }

    [Theory]
    [InlineData(DayOfWeek.Friday, 5)]
    [InlineData(ByteEnum.Two, (byte)2)]
    public void ToProviderValue_Enum_BecomesUnderlyingNumber(object value, object expected)
    {
        var result = QueryExecutor.ToProviderValue(value);

        Assert.Equal(expected, result);
        Assert.Equal(expected.GetType(), result.GetType());
    }

    [Fact]
    public void ToProviderValue_NonEnum_IsUnchanged()
    {
        var value = new object();

        Assert.Same(value, QueryExecutor.ToProviderValue(value));
    }

    public enum ByteEnum : byte
    {
        Two = 2,
    }

    private BoundProperty Property(string name) => ModelBinder.GetEntity(_db.Model, typeof(Item)).GetProperty(name);
}
