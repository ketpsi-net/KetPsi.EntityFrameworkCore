using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class DeleteTests : IDisposable
{
    private readonly FakeDbConnection _connection = new() { RowsAffected = 3 };
    private readonly FakeItemContext _db;

    public DeleteTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ExecuteDelete_ReturnsTheRowsDeletedAndClosesTheConnection()
    {
        var query = SqlQuery.Where(_db.Items, new ComparisonTemplate(nameof(Item.Score), ComparisonOperator.LessThan, 0), [5]);

        Assert.Equal(3, SqlQuery.ExecuteDelete(query));
        Assert.Equal("DELETE FROM [t0] FROM [shop].[Items] AS [t0] WHERE [t0].[Score] < @p0", _connection.LastCommand.CommandText);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_ReturnsTheRowsDeleted()
        => Assert.Equal(3, await SqlQuery.ExecuteDeleteAsync(_db.Items, Token));

    // As in EF Core, deleting never touches the change tracker, so a tracked entity stays as it was.
    [Fact]
    public void ExecuteDelete_LeavesTrackedEntitiesAlone()
    {
        var tracked = new Item { Id = 1, Name = "tracked" };
        _db.Attach(tracked);

        SqlQuery.ExecuteDelete(SqlQuery.AsNoTracking(_db.Items));

        Assert.Equal(EntityState.Unchanged, _db.Entry(tracked).State);
    }
}
