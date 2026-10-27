using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class TerminalTests : IDisposable
{
    private const string Columns = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score]";
    private const string Table = "[shop].[Items] AS [t0]";

    private static readonly Item Alpha = new() { Id = 1, IsActive = true, Name = "alpha", Score = 10 };
    private static readonly Item Beta = new() { Id = 2, IsActive = false, Name = "beta", Score = null };

    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public TerminalTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IQueryable<Item> Items => SqlQuery.AsNoTracking(_db.Items);

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ToList_ReadsEveryRowAndSendsStatement()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        var items = SqlQuery.ToList(Items);

        Assert.Equivalent(new[] { Alpha, Beta }, items, strict: true);
        Assert.Equal("SELECT " + Columns + " FROM " + Table, _connection.LastCommand.CommandText);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public async Task ToListAsync_ReadsEveryRow()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        Assert.Equivalent(new[] { Alpha, Beta }, await SqlQuery.ToListAsync(Items, Token), strict: true);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public void ToArray_ReadsEveryRow()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equivalent(new[] { Alpha }, SqlQuery.ToArray(Items), strict: true);
    }

    [Fact]
    public async Task ToArrayAsync_ReadsEveryRow()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equivalent(new[] { Alpha }, await SqlQuery.ToArrayAsync(Items, Token), strict: true);
    }

    [Fact]
    public void ToList_NoRows_ReturnsEmpty()
    {
        _connection.Rows = FakeItemContext.Table();

        Assert.Empty(SqlQuery.ToList(Items));
    }

    [Fact]
    public void ToList_WithWhere_SendsTypedParameter()
    {
        _connection.Rows = FakeItemContext.Table();
        var predicate = new ComparisonTemplate(nameof(Item.Name), ComparisonOperator.Equal, 0);

        SqlQuery.ToList(SqlQuery.Where(Items, predicate, ["alpha"]));

        var parameter = Assert.Single(_connection.LastCommand.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>());
        Assert.Equal("@p0", parameter.ParameterName);
        Assert.Equal("alpha", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void First_ReturnsFirstRowWithTopOne()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equivalent(Alpha, SqlQuery.First(Items), strict: true);
        Assert.StartsWith("SELECT TOP(1) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstAsync_ReturnsFirstRow()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equivalent(Alpha, await SqlQuery.FirstAsync(Items, Token), strict: true);
    }

    [Fact]
    public void First_NoRows_ThrowsLikeLinq()
    {
        _connection.Rows = FakeItemContext.Table();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.First(Items));
        Assert.Equal(LinqMessage(() => Array.Empty<int>().First()), exception.Message);
    }

    [Fact]
    public async Task FirstAsync_NoRows_Throws()
    {
        _connection.Rows = FakeItemContext.Table();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.FirstAsync(Items, Token));
    }

    [Fact]
    public void FirstOrDefault_NoRows_ReturnsNull()
    {
        _connection.Rows = FakeItemContext.Table();

        Assert.Null(SqlQuery.FirstOrDefault(Items));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsRowOrNull()
    {
        _connection.Rows = FakeItemContext.Table(Beta);
        Assert.Equivalent(Beta, await SqlQuery.FirstOrDefaultAsync(Items, Token), strict: true);

        _connection.Rows = FakeItemContext.Table();
        Assert.Null(await SqlQuery.FirstOrDefaultAsync(Items, Token));
    }

    [Fact]
    public void Single_OneRow_ReturnsItWithTopTwo()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equivalent(Alpha, SqlQuery.Single(Items), strict: true);
        Assert.StartsWith("SELECT TOP(2) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_NoRows_ThrowsLikeLinq()
    {
        _connection.Rows = FakeItemContext.Table();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.Single(Items));
        Assert.Equal(LinqMessage(() => Array.Empty<int>().Single()), exception.Message);
    }

    [Fact]
    public void Single_TwoRows_ThrowsLikeLinq()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.Single(Items));
        Assert.Equal(LinqMessage(() => new[] { 1, 2 }.Single()), exception.Message);
    }

    [Fact]
    public async Task SingleAsync_FollowsSingleRules()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);
        Assert.Equivalent(Alpha, await SqlQuery.SingleAsync(Items, Token), strict: true);

        _connection.Rows = FakeItemContext.Table();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.SingleAsync(Items, Token));

        _connection.Rows = FakeItemContext.Table(Alpha, Beta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.SingleAsync(Items, Token));
    }

    [Fact]
    public void SingleOrDefault_FollowsSingleOrDefaultRules()
    {
        _connection.Rows = FakeItemContext.Table();
        Assert.Null(SqlQuery.SingleOrDefault(Items));

        _connection.Rows = FakeItemContext.Table(Beta);
        Assert.Equivalent(Beta, SqlQuery.SingleOrDefault(Items), strict: true);

        _connection.Rows = FakeItemContext.Table(Alpha, Beta);
        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.SingleOrDefault(Items));
        Assert.Equal(LinqMessage(() => new[] { 1, 2 }.SingleOrDefault()), exception.Message);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_FollowsSingleOrDefaultRules()
    {
        _connection.Rows = FakeItemContext.Table();
        Assert.Null(await SqlQuery.SingleOrDefaultAsync(Items, Token));

        _connection.Rows = FakeItemContext.Table(Alpha, Beta);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.SingleOrDefaultAsync(Items, Token));
    }

    [Fact]
    public void Count_ReturnsScalarAsInt()
    {
        _connection.Scalar = 3;

        Assert.Equal(3, SqlQuery.Count(Items));
        Assert.Equal("SELECT COUNT(*) FROM " + Table, _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task CountAsync_ReturnsScalarAsInt()
    {
        _connection.Scalar = 3;

        Assert.Equal(3, await SqlQuery.CountAsync(Items, Token));
    }

    [Fact]
    public void LongCount_ReturnsScalarAsLong()
    {
        _connection.Scalar = 5_000_000_000L;

        Assert.Equal(5_000_000_000L, SqlQuery.LongCount(Items));
        Assert.Equal("SELECT COUNT_BIG(*) FROM " + Table, _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task LongCountAsync_ReturnsScalarAsLong()
    {
        _connection.Scalar = 7L;

        Assert.Equal(7L, await SqlQuery.LongCountAsync(Items, Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Any_ReturnsScalarAsBool(bool exists)
    {
        _connection.Scalar = exists;

        Assert.Equal(exists, SqlQuery.Any(Items));
        Assert.StartsWith("SELECT CASE WHEN EXISTS (", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyAsync_ReturnsScalarAsBool()
    {
        _connection.Scalar = true;

        Assert.True(await SqlQuery.AnyAsync(Items, Token));
    }

    [Fact]
    public void RowTerminals_WithoutNoTracking_Throw()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.ToList(_db.Items));

        Assert.Contains("AsNoTracking", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_connection.Commands);
        Assert.Throws<InvalidOperationException>(() => SqlQuery.First(_db.Items));
        Assert.Throws<InvalidOperationException>(() => SqlQuery.SingleOrDefault(_db.Items));
    }

    [Fact]
    public async Task AsyncRowTerminals_WithoutNoTracking_Throw()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.ToListAsync(_db.Items, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlQuery.FirstOrDefaultAsync(_db.Items, Token));
    }

    [Fact]
    public void RowTerminals_ContextDefaultsToNoTracking_Run()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);
        using var db = new FakeItemContext(_connection, QueryTrackingBehavior.NoTracking);

        Assert.Single(SqlQuery.ToList(db.Items));
    }

    [Fact]
    public void ScalarTerminals_DoNotNeedNoTracking()
    {
        _connection.Scalar = 1;

        Assert.Equal(1, SqlQuery.Count(_db.Items));
        Assert.True(SqlQuery.Any(_db.Items));
    }

    [Fact]
    public void Enumerating_ExecutesQuery()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        var names = new List<string>();
        foreach (var item in Items)
        {
            names.Add(item.Name);
        }

        Assert.Equal(["alpha", "beta"], names);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public void Enumerating_StoppedEarly_ClosesConnection()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        foreach (var _ in Items)
        {
            break;
        }

        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public async Task EnumeratingAsync_ExecutesQuery()
    {
        _connection.Rows = FakeItemContext.Table(Alpha, Beta);

        var names = new List<string>();
        await foreach (var item in ((IAsyncEnumerable<Item>)Items).WithCancellation(Token))
        {
            names.Add(item.Name);
        }

        Assert.Equal(["alpha", "beta"], names);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public void Enumerating_WithoutNoTracking_Throws()
        => Assert.Throws<InvalidOperationException>(() => SqlQuery.ToSqlQueryable(_db.Items).GetEnumerator());

    [Fact]
    public async Task AsyncTerminal_CanceledToken_ThrowsWithoutQuerying()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlQuery.ToListAsync(Items, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlQuery.CountAsync(Items, canceled.Token));
        Assert.Empty(_connection.Commands);
    }

    [Fact]
    public void Terminal_EntityWithoutReader_Throws()
    {
        using var db = new TestDbContext();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.ToList(SqlQuery.AsNoTracking(db.Blogs)));

        Assert.Contains(typeof(Blog).ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static string LinqMessage(Action action) => Assert.Throws<InvalidOperationException>(action).Message;
}
