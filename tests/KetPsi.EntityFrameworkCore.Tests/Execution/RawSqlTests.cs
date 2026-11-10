using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary>SQL of <c>FromSql</c> sent as it is: its columns are read by name, as EF Core reads them.</summary>
public sealed class RawSqlTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public RawSqlTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection, QueryTrackingBehavior.NoTracking);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Uncomposed_ReadsColumnsByNameInAnyOrderAndCase()
    {
        _connection.Rows = Table(("score", typeof(int)), ("Extra", typeof(string)), ("NAME", typeof(string)), ("Id", typeof(int)), ("isactive", typeof(bool)));
        _connection.Rows.Rows.Add(10, "ignored", "alpha", 1, true);

        var items = SqlQuery.ToList(SqlQuery.FromSqlRaw(_db.Items, "EXEC GetItems", null));
        var again = await SqlQuery.ToListAsync(SqlQuery.FromSqlRaw(_db.Items, "EXEC GetItems", null), Token);

        var item = Assert.Single(items);
        Assert.Equal((1, true, "alpha", (int?)10), (item.Id, item.IsActive, item.Name, item.Score));
        Assert.Equal("alpha", Assert.Single(again).Name);
        Assert.Equal("EXEC GetItems", _connection.LastCommand.CommandText);
    }

    [Fact]
    public void Uncomposed_MissingColumn_ThrowsLikeEfCore()
    {
        _connection.Rows = Table(("Id", typeof(int)), ("Name", typeof(string)));

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.ToList(SqlQuery.FromSqlRaw(_db.Items, "SELECT Id, Name FROM Items", null)));

        Assert.Equal("The required column 'IsActive' was not present in the results of a 'FromSql' operation.", exception.Message);
    }

    [Fact]
    public void Tracked_Uncomposed_AttachesTheEntities()
    {
        using var tracking = new FakeItemContext(_connection);
        _connection.Rows = Table(("Name", typeof(string)), ("Id", typeof(int)), ("IsActive", typeof(bool)), ("Score", typeof(int)));
        _connection.Rows.Rows.Add("alpha", 1, true, 10);

        var item = Assert.Single(SqlQuery.ToList(SqlQuery.FromSqlRaw(tracking.Items, "SELECT Name, Id, IsActive, Score FROM Items", null)));

        Assert.Equal(EntityState.Unchanged, tracking.Entry(item).State);
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type).AllowDBNull = true;
        }

        return table;
    }
}
