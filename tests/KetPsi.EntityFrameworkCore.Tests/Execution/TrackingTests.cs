using System.Data;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class TrackingTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly TrackedItemContext _db;

    public TrackingTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new TrackedItemContext(_connection);
        _connection.Rows = Table((1, "alpha", Priority.High, 7), (2, "beta", null, null));
    }

    public enum Priority
    {
        Low,
        High,
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ToList_AttachesEntitiesAsUnchanged()
    {
        var items = SqlQuery.ToList(_db.Items);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(EntityState.Unchanged, _db.Entry(item).State));
        Assert.Equal(2, _db.ChangeTracker.Entries<Item>().Count());
    }

    [Fact]
    public void SecondQuery_ReturnsTrackedInstanceWithoutRefreshing()
    {
        var first = SqlQuery.First(_db.Items);
        first.Name = "changed";
        _connection.Rows = Table((1, "fresh", Priority.Low, 99));

        var second = SqlQuery.First(_db.Items);

        Assert.Same(first, second);
        Assert.Equal("changed", second.Name);
        Assert.Equal(7, _db.Entry(second).Property("Revision").CurrentValue);
        Assert.Equal(EntityState.Modified, _db.Entry(second).State);
    }

    [Fact]
    public void ShadowValues_AreCurrentAndOriginal()
    {
        var entry = _db.Entry(SqlQuery.First(_db.Items));

        Assert.Equal(7, entry.Property("Revision").CurrentValue);
        Assert.Equal(7, entry.Property("Revision").OriginalValue);
        Assert.Equal(Priority.High, entry.Property("Priority").CurrentValue);
        Assert.Equal(Priority.High, entry.Property("Priority").OriginalValue);
    }

    [Fact]
    public void ShadowValues_Null_AreNull()
    {
        var entry = _db.Entry(SqlQuery.ToList(_db.Items)[1]);

        Assert.Null(entry.Property("Revision").CurrentValue);
        Assert.Null(entry.Property("Priority").CurrentValue);
    }

    [Fact]
    public void ChangingShadowValue_MarksOnlyThatPropertyModified()
    {
        var entry = _db.Entry(SqlQuery.First(_db.Items));

        entry.Property("Revision").CurrentValue = 8;
        _db.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(["Revision"], entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name));
    }

    [Fact]
    public void AsNoTracking_DoesNotAttach()
    {
        var items = SqlQuery.ToList(SqlQuery.AsNoTracking(_db.Items));

        Assert.Equal(2, items.Count);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void AsTrackingAfterAsNoTracking_Attaches()
    {
        SqlQuery.ToList(SqlQuery.AsTracking(SqlQuery.AsNoTracking(_db.Items)));

        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void AsNoTrackingAfterAsTracking_DoesNotAttach()
    {
        SqlQuery.ToList(SqlQuery.AsNoTracking(SqlQuery.AsTracking(_db.Items)));

        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void AsTrackingWithBehavior_FollowsBehavior()
    {
        SqlQuery.ToList(SqlQuery.AsTracking(_db.Items, QueryTrackingBehavior.NoTracking));
        Assert.Empty(_db.ChangeTracker.Entries());

        SqlQuery.ToList(SqlQuery.AsTracking(_db.Items, QueryTrackingBehavior.TrackAll));
        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void ContextDefaultNoTracking_DoesNotAttachUnlessAsTracking()
    {
        using var db = new TrackedItemContext(_connection, QueryTrackingBehavior.NoTracking);

        SqlQuery.ToList(db.Items);
        Assert.Empty(db.ChangeTracker.Entries());

        SqlQuery.ToList(SqlQuery.AsTracking(db.Items));
        Assert.Equal(2, db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void KeylessEntity_IsNotTracked()
    {
        _connection.Rows = FakeItemContext.Table(new Item { Id = 1, Name = "alpha" });
        using var db = new KeylessItemContext(_connection);

        Assert.Single(SqlQuery.ToList(db.Items));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Enumerating_Attaches()
    {
        foreach (var item in SqlQuery.ToSqlQueryable(_db.Items))
        {
            Assert.Equal(EntityState.Unchanged, _db.Entry(item).State);
        }

        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public async Task AsyncTerminals_Attach()
    {
        var items = await SqlQuery.ToListAsync(_db.Items, Token);
        var first = await SqlQuery.FirstAsync(_db.Items, Token);

        Assert.Same(items[0], first);
        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public async Task EnumeratingAsync_Attaches()
    {
        await foreach (var item in ((IAsyncEnumerable<Item>)SqlQuery.ToSqlQueryable(_db.Items)).WithCancellation(Token))
        {
            Assert.Equal(EntityState.Unchanged, _db.Entry(item).State);
        }

        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void IdentityResolution_SameKeyInOneQuery_ReturnsFirstInstance()
    {
        _connection.Rows = Table((1, "alpha", null, 7), (2, "beta", null, null), (1, "again", null, 8));

        var items = SqlQuery.ToList(SqlQuery.AsNoTrackingWithIdentityResolution(_db.Items));

        Assert.Equal(3, items.Count);
        Assert.Same(items[0], items[2]);
        Assert.NotSame(items[0], items[1]);
        Assert.Equal("alpha", items[2].Name);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void IdentityResolution_SeparateExecutions_ReturnNewInstances()
    {
        var query = SqlQuery.AsNoTrackingWithIdentityResolution(_db.Items);

        var first = SqlQuery.First(query);
        first.Name = "changed";
        var second = SqlQuery.First(query);

        Assert.NotSame(first, second);
        Assert.Equal("alpha", second.Name);
    }

    [Fact]
    public void IdentityResolution_IgnoresTrackedEntities()
    {
        var tracked = SqlQuery.First(_db.Items);
        tracked.Name = "changed";
        var trackedCount = _db.ChangeTracker.Entries().Count();

        var resolved = SqlQuery.First(SqlQuery.AsNoTrackingWithIdentityResolution(_db.Items));

        Assert.NotSame(tracked, resolved);
        Assert.Equal("alpha", resolved.Name);
        Assert.Equal(trackedCount, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void IdentityResolution_ContextDefault_Resolves()
    {
        _connection.Rows = Table((1, "alpha", null, null), (1, "again", null, null));
        using var db = new TrackedItemContext(_connection, QueryTrackingBehavior.NoTrackingWithIdentityResolution);

        var items = SqlQuery.ToList(db.Items);

        Assert.Same(items[0], items[1]);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void IdentityResolution_ThenAsTracking_Attaches()
    {
        SqlQuery.ToList(SqlQuery.AsTracking(SqlQuery.AsNoTrackingWithIdentityResolution(_db.Items)));

        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void IdentityResolution_KeylessEntity_ReturnsEveryRow()
    {
        _connection.Rows = FakeItemContext.Table(new Item { Id = 1, Name = "alpha" }, new Item { Id = 1, Name = "alpha" });
        using var db = new KeylessItemContext(_connection);

        var items = SqlQuery.ToList(SqlQuery.AsNoTrackingWithIdentityResolution(db.Items));

        Assert.NotSame(items[0], items[1]);
    }

    [Fact]
    public async Task IdentityResolution_Async_Resolves()
    {
        _connection.Rows = Table((1, "alpha", null, null), (1, "again", null, null));

        var items = await SqlQuery.ToListAsync(SqlQuery.AsNoTrackingWithIdentityResolution(_db.Items), Token);

        Assert.Same(items[0], items[1]);
    }

    [Fact]
    public void ResolveTracking_NoTrackingNode_ReturnsContextDefault()
    {
        var root = new RootNode(typeof(Item));

        Assert.Equal(QueryTrackingBehavior.NoTracking, QueryRunner.ResolveTracking(root, QueryTrackingBehavior.NoTracking));
        Assert.Equal(QueryTrackingBehavior.TrackAll, QueryRunner.ResolveTracking(root, QueryTrackingBehavior.TrackAll));
    }

    [Fact]
    public void ResolveTracking_ReturnsNodeNearestTail()
    {
        var root = new RootNode(typeof(Item));
        var tail = new TrackingNode(new TrackingNode(root, QueryTrackingBehavior.NoTracking), QueryTrackingBehavior.TrackAll);

        Assert.Equal(QueryTrackingBehavior.TrackAll, QueryRunner.ResolveTracking(tail, QueryTrackingBehavior.NoTracking));
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(true, 10)]
    [InlineData(false, 9)]
    [InlineData(false, 10)]
    public void EnsureTrackingSupported_Supported_DoesNotThrow(bool isDynamicCodeSupported, int efCoreMajorVersion)
        => QueryRunner.EnsureTrackingSupported(isDynamicCodeSupported, efCoreMajorVersion);

    [Fact]
    public void EnsureTrackingSupported_AotBeforeEfCore9_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QueryRunner.EnsureTrackingSupported(false, 8));

        Assert.Contains("AsNoTracking", exception.Message, StringComparison.Ordinal);
    }

    // Statements select the key first, then the other properties in name order, shadow properties included.
    private static DataTable Table(params (int Id, string Name, Priority? Priority, int? Revision)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(Item.Id), typeof(int));
        table.Columns.Add(nameof(Item.IsActive), typeof(bool));
        table.Columns.Add(nameof(Item.Name), typeof(string));
        table.Columns.Add("Priority", typeof(int)).AllowDBNull = true;
        table.Columns.Add("Revision", typeof(int)).AllowDBNull = true;
        table.Columns.Add(nameof(Item.Score), typeof(int)).AllowDBNull = true;
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.Id,
                true,
                row.Name,
                row.Priority is { } priority ? (int)priority : DBNull.Value,
                row.Revision is { } revision ? revision : DBNull.Value,
                DBNull.Value);
        }

        return table;
    }

    private sealed class TrackedItemContext(FakeDbConnection connection, QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll)
        : DbContext
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlServer(connection).UseQueryTrackingBehavior(tracking);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var item = modelBuilder.Entity<Item>().ToTable("Items", "shop");
            item.Property<int?>("Revision");
            item.Property<Priority?>("Priority");
        }
    }

    private sealed class KeylessItemContext(FakeDbConnection connection) : DbContext
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(connection);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Item>().HasNoKey().ToTable("Items", "shop");
    }
}
