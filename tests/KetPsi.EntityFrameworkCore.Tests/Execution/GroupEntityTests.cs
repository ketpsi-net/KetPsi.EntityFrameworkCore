using System.Data;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary>Groups of entities returned directly behave as the EF Core probe showed, per tracking behavior.</summary>
public sealed class GroupEntityTests
{
    private readonly FakeDbConnection _connection = new();

    public GroupEntityTests()
    {
        FakeLibraryContext.EnsureRegistered();
    }

    [Fact]
    public void Groups_ReadTheKeyThenTheEntity_OrderedByTheKey()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Books(db, (1, 10), (2, 10), (3, 20)));

        var groups = SqlGrouping.ToList(ByWriter(db.Books));

        Assert.Equal([10, 20], groups.Select(static g => g.Key));
        Assert.Equal([1, 2], groups[0].Select(static b => b.Id));
        Assert.Equal([3], groups[1].Select(static b => b.Id));
        Assert.Equal(10, groups[0].First().WriterId);
        Assert.StartsWith("SELECT [t0].[WriterId], [t0].[Id], ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.EndsWith(" FROM [Books] AS [t0] ORDER BY [t0].[WriterId]", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Tracking_AttachesTheGroupedEntities()
    {
        using var db = Context();
        Queue(Books(db, (1, 10), (2, 10), (3, 20)));

        var groups = SqlGrouping.ToList(ByWriter(db.Books));

        Assert.Equal(3, db.ChangeTracker.Entries().Count());
        Assert.All(groups.SelectMany(static g => g), b => Assert.Equal(EntityState.Unchanged, db.Entry(b).State));
    }

    [Fact]
    public void Tracking_ReturnsTheEntityAlreadyTracked()
    {
        using var db = Context();
        var tracked = new Book { Id = 1, WriterId = 10 };
        db.Attach(tracked);
        Queue(Books(db, (1, 10)));

        var group = Assert.Single(SqlGrouping.ToList(ByWriter(db.Books)));

        Assert.Same(tracked, Assert.Single(group));
    }

    [Fact]
    public void IdentityResolution_SharesInstancesAcrossGroups()
    {
        using var db = Context(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        Queue(Books(db, (1, 10), (1, 10)));

        var group = Assert.Single(SqlGrouping.ToList(ByWriter(db.Books)));

        Assert.Same(group.First(), group.Last());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Include_LoadsTheGroupedEntitiesReferences()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var table = Table(db, 1, typeof(Book), typeof(Writer))
            .Add(db, [10], (typeof(Book), [("Id", 1), ("WriterId", 10)]), (typeof(Writer), [("Id", 10), ("Name", "w")]));
        Queue(table);

        var groups = SqlGrouping.ToList(ByWriter(Include(db.Books, nameof(Book.Writer))));

        Assert.Equal("w", Assert.Single(Assert.Single(groups)).Writer.Name);
        Assert.Contains("INNER JOIN [Writers] AS [t1] ON [t0].[WriterId] = [t1].[Id]", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    private static IQueryable<IGrouping<int, Book>> ByWriter(IQueryable<Book> books)
        => SqlGrouping.GroupBy<Book, int, Book>(
            books,
            [nameof(Book.WriterId)],
            static (reader, ordinals, _) => Convert.ToInt32(reader.GetValue(ordinals[0]), System.Globalization.CultureInfo.InvariantCulture),
            [],
            [""],
            static (_, _, entities) => (Book)entities[0]!);

    private static IQueryable<T> Include<T>(IQueryable<T> source, params string[] path)
        where T : class
    {
        var query = SqlQuery.ToSqlQueryable(source);
        return query.WithTail(new IncludeNode(query.Tail, path));
    }

    private FakeLibraryContext Context(QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll) => new(_connection, tracking);

    private void Queue(params DataTable[] tables)
    {
        foreach (var table in tables)
        {
            _connection.RowSets.Enqueue(table);
        }
    }

    private static DataTable Books(DbContext db, params (int Id, int WriterId)[] books)
    {
        var table = Table(db, 1, typeof(Book));
        foreach (var (id, writerId) in books)
        {
            table.Add(db, [writerId], (typeof(Book), [("Id", id), ("WriterId", writerId)]));
        }

        return table;
    }

    // The statement selects the key columns first, then each entity's columns in model order.
    private static DataTable Table(DbContext db, int columns, params Type[] entities)
    {
        var table = new DataTable();
        var count = columns + entities.Sum(type => ModelBinder.GetEntity(db.Model, type).Properties.Count);
        for (var i = 0; i < count; i++)
        {
            table.Columns.Add("c" + i, typeof(object)).AllowDBNull = true;
        }

        return table;
    }
}
