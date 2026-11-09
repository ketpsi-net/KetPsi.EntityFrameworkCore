using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary><c>Find</c> and <c>FindAsync</c>, which behave as EF Core 10 does on SQL Server.</summary>
public sealed class FindTests : IDisposable
{
    private const string Columns = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score]";

    private static readonly Item Alpha = new() { Id = 1, IsActive = true, Name = "alpha", Score = 10 };

    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public FindTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Find_QueriesByKeyAndTracksTheEntity()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        var item = SqlQuery.Find(_db.Items, [1]);

        Assert.Equal(Alpha.Name, item!.Name);
        Assert.Equal("SELECT TOP(1) " + Columns + " FROM [shop].[Items] AS [t0] WHERE [t0].[Id] = @p0", _connection.LastCommand.CommandText);
        Assert.Equal(EntityState.Unchanged, _db.Entry(item).State);
    }

    [Fact]
    public void Find_NoRow_ReturnsNull()
    {
        _connection.Rows = FakeItemContext.Table();

        Assert.Null(SqlQuery.Find(_db.Items, [9]));
    }

    [Theory]
    [InlineData(EntityState.Unchanged)]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Deleted)]
    public async Task Find_TrackedEntity_IsReturnedWithoutAQuery(EntityState state)
    {
        var tracked = new Item { Id = 5, Name = "tracked" };
        _db.Attach(tracked).State = state;

        Assert.Same(tracked, SqlQuery.Find(_db.Items, [5]));
        Assert.Same(tracked, await SqlQuery.FindAsync(_db.Items, [5], Token));
        Assert.Empty(_connection.Commands);
    }

    [Fact]
    public void Find_UsesTheContextsDefaultTracking()
    {
        using var noTracking = new FakeItemContext(_connection, QueryTrackingBehavior.NoTracking);
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.NotNull(SqlQuery.Find(noTracking.Items, [1]));
        Assert.Empty(noTracking.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Find_NullKeyValues_ReturnNullWithoutAQuery()
    {
        Assert.Null(SqlQuery.Find(_db.Items, null));
        Assert.Null(SqlQuery.Find(_db.Items, [null]));
        Assert.Null(await SqlQuery.FindAsync(_db.Items, null));
        Assert.Empty(_connection.Commands);
    }

    [Fact]
    public void Find_NullableValueOfTheKeyType_IsAccepted()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.NotNull(SqlQuery.Find(_db.Items, [(int?)1]));
    }

    [Theory]
    [InlineData(new object[] { 1, 2 }, "Entity type 'Item' is defined with a single key property, but 2 values were passed to the 'Find' method.")]
    [InlineData(new object[0], "Entity type 'Item' is defined with a single key property, but 0 values were passed to the 'Find' method.")]
    [InlineData(new object[] { 1L }, "The key value at position 0 of the call to 'DbSet<Item>.Find' was of type 'long', which does not match the property type of 'int'.")]
    [InlineData(new object[] { "1" }, "The key value at position 0 of the call to 'DbSet<Item>.Find' was of type 'string', which does not match the property type of 'int'.")]
    public void Find_WrongKeyValues_ThrowLikeEfCore(object[] keyValues, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => SqlQuery.Find(_db.Items, keyValues));

        Assert.Equal(message, exception.Message);
        Assert.Null(exception.ParamName);
    }

    // EF Core validates when FindAsync is called, so the exception isn't in the returned task.
    [Fact]
    public void FindAsync_WrongKeyValues_ThrowsWhenCalled()
        => Assert.Throws<ArgumentException>(() => SqlQuery.FindAsync(_db.Items, [1, 2], Token));

    [Fact]
    public async Task FindAsync_QueriesByKey()
    {
        _connection.Rows = FakeItemContext.Table(Alpha);

        Assert.Equal(Alpha.Name, (await SqlQuery.FindAsync(_db.Items, [1], Token))!.Name);
        Assert.EndsWith("WHERE [t0].[Id] = @p0", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_CompositeKey_ValidatesEachPartAndFindsTrackedEntities()
    {
        using var db = new CompositeKeyContext();
        var tracked = new Enrollment { StudentId = 1, CourseId = 2 };
        db.Attach(tracked);

        Assert.Same(tracked, SqlQuery.Find(db.Set<Enrollment>(), [1, 2]));
        Assert.Equal(
            "Entity type 'Enrollment' is defined with a 2-part composite key, but 1 values were passed to the 'Find' method.",
            Assert.Throws<ArgumentException>(() => SqlQuery.Find(db.Set<Enrollment>(), [1])).Message);
        Assert.Equal(
            "The key value at position 1 of the call to 'DbSet<Enrollment>.Find' was of type 'short', which does not match the property type of 'int'.",
            Assert.Throws<ArgumentException>(() => SqlQuery.Find(db.Set<Enrollment>(), [1, (short)2])).Message);
    }

    [Fact]
    public void Find_KeylessEntity_Throws()
    {
        using var db = new KeylessContext();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.Find(db.Set<Report>(), [1]));

        Assert.StartsWith(
            "The invoked method cannot be used for the entity type 'Report' because it does not have a primary key.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
