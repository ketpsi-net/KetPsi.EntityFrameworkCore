using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class UpdateTests : IDisposable
{
    private readonly FakeDbConnection _connection = new() { RowsAffected = 3 };
    private readonly FakeItemContext _db;

    public UpdateTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ExecuteUpdate_ReturnsTheRowsUpdatedAndClosesTheConnection()
    {
        var query = SqlQuery.Where(_db.Items, new ComparisonTemplate(nameof(Item.Score), ComparisonOperator.LessThan, 0), [5]);

        Assert.Equal(3, SqlQuery.ExecuteUpdate(query, [new UpdateSetter(nameof(Item.Name), new SlotValueTemplate(0), ["x"])]));
        Assert.Equal(
            $"{Sql.SqlBuilderDeleteTests.NoCountOff}UPDATE [t0] SET [t0].[Name] = @p1 FROM [shop].[Items] AS [t0] WHERE [t0].[Score] < @p0",
            _connection.LastCommand.CommandText);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public async Task ExecuteUpdateAsync_ReturnsTheRowsUpdated()
        => Assert.Equal(3, await SqlQuery.ExecuteUpdateAsync(_db.Items, [new UpdateSetter(nameof(Item.Score), new SlotValueTemplate(0), [1])], Token));

    // As in EF Core, updating never touches the change tracker, so a tracked entity keeps its values.
    [Fact]
    public void ExecuteUpdate_LeavesTrackedEntitiesAlone()
    {
        var tracked = new Item { Id = 1, Name = "tracked" };
        _db.Attach(tracked);

        SqlQuery.ExecuteUpdate(_db.Items, [new UpdateSetter(nameof(Item.Name), new SlotValueTemplate(0), ["x"])]);

        Assert.Equal("tracked", tracked.Name);
        Assert.Equal(EntityState.Unchanged, _db.Entry(tracked).State);
    }
}
