using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class AggregateTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public AggregateTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    public enum Level
    {
        Low,
        High,
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Aggregate_OfEntityProperty_RunsScalarStatement()
    {
        _connection.Scalar = 12;

        Assert.Equal(12, SqlQuery.Aggregate<Item, int>(_db.Items, AggregateFunction.Sum, nameof(Item.Score), AggregateValueType.Int32));
        Assert.Equal("SELECT COALESCE(SUM([t0].[Score]), 0) FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task AggregateAsync_OfEntityProperty()
    {
        _connection.Scalar = 2.5;

        Assert.Equal(2.5, await SqlQuery.AggregateAsync<Item, double>(_db.Items, AggregateFunction.Average, nameof(Item.Score), AggregateValueType.Int32, Token));
        Assert.Equal("SELECT AVG(CAST([t0].[Score] AS float)) FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task Aggregate_OfProjection_SyncAndAsync()
    {
        _connection.Scalar = "zeta";
        var names = SqlQuery.Select(_db.Items, [nameof(Item.Name)], static (reader, ordinals) => reader.GetString(ordinals[0]));

        Assert.Equal("zeta", SqlProjection.Aggregate<string, string>(names, AggregateFunction.Max, AggregateValueType.Column));
        Assert.Equal("zeta", await SqlProjection.AggregateAsync<string, string>(names, AggregateFunction.Max, AggregateValueType.Column, Token));
        Assert.Equal("SELECT MAX([t0].[Name]) FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public void Aggregate_NoRowsForValueType_ThrowsLikeTheEfCoreVersion()
    {
        _connection.Scalar = DBNull.Value;

        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlQuery.Aggregate<Item, int>(_db.Items, AggregateFunction.Max, nameof(Item.Id), AggregateValueType.Column));

        Assert.Equal(
            QueryRunner.EfCoreMajorVersion >= 10 ? "Nullable object must have a value." : "Sequence contains no elements.",
            exception.Message);
    }

    [Fact]
    public void Aggregate_NoRowsForNullable_IsNull()
    {
        _connection.Scalar = DBNull.Value;

        Assert.Null(SqlQuery.Aggregate<Item, int?>(_db.Items, AggregateFunction.Max, nameof(Item.Score), AggregateValueType.Column));
    }

    [Fact]
    public void Aggregate_EmptyPropertyName_Throws()
        => Assert.ThrowsAny<ArgumentException>(
            () => SqlQuery.Aggregate<Item, int>(_db.Items, AggregateFunction.Max, "", AggregateValueType.Column));

    [Theory]
    [InlineData(8, "Sequence contains no elements.")]
    [InlineData(9, "Sequence contains no elements.")]
    [InlineData(10, "Nullable object must have a value.")]
    public void Convert_NullToValueType_UsesThatVersionsMessage(int efCoreMajorVersion, string message)
    {
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => AggregateExecutor.Convert<int>(DBNull.Value, efCoreMajorVersion)).Message);
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => AggregateExecutor.Convert<DateTime>(null, efCoreMajorVersion)).Message);
    }

    [Fact]
    public void Convert_NullToNullableOrReference_IsNull()
    {
        Assert.Null(AggregateExecutor.Convert<decimal?>(DBNull.Value, 10));
        Assert.Null(AggregateExecutor.Convert<string>(DBNull.Value, 10));
    }

    [Fact]
    public void Convert_SameType_IsUnchanged()
    {
        Assert.Equal(1.5m, AggregateExecutor.Convert<decimal>(1.5m, 10));
        Assert.Equal(7, AggregateExecutor.Convert<int?>(7, 10));
    }

    [Fact]
    public void Convert_Enum_FromItsNumber()
    {
        Assert.Equal(Level.High, AggregateExecutor.Convert<Level>(1, 10));
        Assert.Equal(Level.High, AggregateExecutor.Convert<Level?>(1, 10));
    }

    [Fact]
    public void Convert_DateAndTime_FromProviderTypes()
    {
        Assert.Equal(new DateOnly(2024, 3, 4), AggregateExecutor.Convert<DateOnly>(new DateTime(2024, 3, 4), 10));
        Assert.Equal(new TimeOnly(8, 30), AggregateExecutor.Convert<TimeOnly?>(new TimeSpan(8, 30, 0), 10));
    }

    [Fact]
    public void Convert_OtherNumericTypes_Convert()
    {
        Assert.Equal(5L, AggregateExecutor.Convert<long>(5, 10));
        Assert.Equal(2.5f, AggregateExecutor.Convert<float>(2.5, 10));
    }
}
