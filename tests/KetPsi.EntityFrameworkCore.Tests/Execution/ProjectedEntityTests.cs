using System.Data;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary>Entities selected by a projection behave as the EF Core probe showed, per tracking behavior.</summary>
public sealed class ProjectedEntityTests
{
    private readonly FakeDbConnection _connection = new();

    public ProjectedEntityTests()
    {
        FakeLibraryContext.EnsureRegistered();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void NoTracking_EachProjectedEntityIsItsOwnInstance_AndNothingIsConnected()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(BooksAndWriters(db, (1, 10), (2, 10)));

        var rows = SqlProjection.ToList(BookAndWriter(db.Books));

        Assert.NotSame(rows[0].Writer, rows[1].Writer);
        Assert.All(rows, r => Assert.Null(r.Book.Writer));
        Assert.All(rows, r => Assert.Null(r.Writer!.Books));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void IdentityResolution_SharesInstances_AndFixesUpNavigations()
    {
        using var db = Context(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        Queue(BooksAndWriters(db, (1, 10), (2, 10)));

        var rows = SqlProjection.ToList(BookAndWriter(db.Books));

        Assert.Same(rows[0].Writer, rows[1].Writer);
        Assert.All(rows, r => Assert.Same(r.Writer, r.Book.Writer));
        Assert.Equal([1, 2], rows[0].Writer!.Books!.Select(b => b.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Tracking_AttachesProjectedEntities_WhichEfCoreFixesUp()
    {
        using var db = Context();
        Queue(BooksAndWriters(db, (1, 10), (2, 10)));

        var rows = SqlProjection.ToList(BookAndWriter(db.Books));

        Assert.Same(rows[0].Writer, rows[1].Writer);
        Assert.All(rows, r => Assert.Same(r.Writer, r.Book.Writer));
        Assert.All(rows, r => Assert.True(db.Entry(r.Book).Reference(b => b.Writer).IsLoaded));
        Assert.Equal(3, db.ChangeTracker.Entries().Count());
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    [Fact]
    public void Tracking_ReturnsTheEntityAlreadyTracked_WithoutRefreshingIt()
    {
        using var db = Context();
        var tracked = new Writer { Id = 10, Name = "changed" };
        db.Attach(tracked);
        Queue(BooksAndWriters(db, (1, 10)));

        var row = Assert.Single(SqlProjection.ToList(BookAndWriter(db.Books)));

        Assert.Same(tracked, row.Writer);
        Assert.Equal("changed", row.Writer!.Name);
    }

    [Fact]
    public void MissingOptionalNavigation_IsNull()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Table(db, 0, typeof(Editor)).Add(db, [], (typeof(Editor), [])));

        var editors = SqlProjection.ToList(SqlQuery.Select(db.Books, [], ["Editor"], static (_, _, entities) => (Editor?)entities[0]));

        Assert.Equal([null], editors);
        Assert.Equal(
            "SELECT [t1].[Id] FROM [Books] AS [t0] LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id]",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void ColumnsAndEntities_ReachTheReaderTogether()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Table(db, 1, typeof(Book)).Add(db, ["w10"], (typeof(Book), [("Id", 1), ("WriterId", 10)])));

        var row = Assert.Single(SqlProjection.ToList(SqlQuery.Select(
            db.Books, ["Writer.Name"], [""], static (reader, ordinals, entities) => (Name: reader.GetString(ordinals[0]), Book: (Book)entities[0]!))));

        Assert.Equal("w10", row.Name);
        Assert.Equal(1, row.Book.Id);
    }

    [Fact]
    public void CollectionIncludedOnAProjectedEntity_IsLoaded()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(
            Table(db, 0, typeof(Writer)).Add(db, [], (typeof(Writer), [("Id", 10)])),
            Table(db, 0, typeof(Book)).Add(db, [], (typeof(Book), [("Id", 1), ("WriterId", 10)])).Add(db, [], (typeof(Book), [("Id", 2), ("WriterId", 10)])));

        var writer = Assert.Single(SqlProjection.ToList(SqlQuery.Select(
            Include(db.Books, nameof(Book.Writer), nameof(Writer.Books)), [], ["Writer"], static (_, _, entities) => (Writer)entities[0]!)));

        Assert.Equal([1, 2], writer.Books!.Select(b => b.Id));
        Assert.All(writer.Books!, b => Assert.Same(writer, b.Writer));
        Assert.Equal(2, _connection.Commands.Count);
    }

    [Theory]
    [InlineData(QueryTrackingBehavior.NoTracking, false)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, true)]
    [InlineData(QueryTrackingBehavior.TrackAll, true)]
    public void RepeatedEntityPath_IsOneInstanceOnlyWithIdentities(QueryTrackingBehavior tracking, bool same)
    {
        using var db = Context(tracking);
        Queue(Table(db, 0, typeof(Writer)).Add(db, [], (typeof(Writer), [("Id", 10)])));

        var (first, second) = Assert.Single(SqlProjection.ToList(SqlQuery.Select(
            db.Books, [], ["Writer", "Writer"], static (_, _, entities) => ((Writer)entities[0]!, (Writer)entities[1]!))));

        Assert.Equal(same, ReferenceEquals(first, second));
    }

    [Fact]
    public async Task Async_ReadsProjectedEntities()
    {
        using var db = Context(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        Queue(BooksAndWriters(db, (1, 10)), BooksAndWriters(db, (1, 10)));

        var rows = await SqlProjection.ToListAsync(BookAndWriter(db.Books), Token);
        var first = await SqlProjection.FirstAsync(BookAndWriter(db.Books), Token);

        Assert.Same(rows[0].Writer, rows[0].Book.Writer);
        Assert.Equal(10, first.Writer!.Id);
        Assert.StartsWith("SELECT TOP(1) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enumerating_ReadsProjectedEntities()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(BooksAndWriters(db, (1, 10), (2, 20)), BooksAndWriters(db, (1, 10)));

        var ids = new List<int>();
        foreach (var row in BookAndWriter(db.Books))
        {
            ids.Add(row.Writer!.Id);
        }

        await foreach (var row in ((IAsyncEnumerable<(Book Book, Writer? Writer)>)BookAndWriter(db.Books)).WithCancellation(Token))
        {
            ids.Add(row.Writer!.Id);
        }

        Assert.Equal([10, 20, 10], ids);
    }

    [Fact]
    public void ColumnsThroughNavigations_Stream()
    {
        using var db = Context();
        Queue(Table(db, 1).Add(db, ["w10"]).Add(db, [DBNull.Value]));

        var names = SqlProjection.ToList(SqlQuery.Select(
            db.Books, ["Writer.Name"], [], static (reader, ordinals, _) => reader.IsDBNull(ordinals[0]) ? null : reader.GetString(ordinals[0])));

        Assert.Equal(["w10", null], names);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Count_IgnoresTheProjection()
    {
        using var db = Context();
        _connection.Scalar = 3;

        Assert.Equal(3, SqlProjection.Count(BookAndWriter(db.Books)));
        Assert.Equal("SELECT COUNT(*) FROM [Books] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public void NoTracking_CycleInTheProjectedEntitysIncludes_Throws()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);

        Assert.Throws<InvalidOperationException>(() => SqlProjection.ToList(SqlQuery.Select(
            Include(db.Books, nameof(Book.Writer), nameof(Writer.Books)), [], [""], static (_, _, entities) => entities[0])));
    }

    [Fact]
    public void NoTracking_CycleOnlyThroughAnUnprojectedEntity_IsAllowed_AsInEfCore()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Table(db, 0, typeof(Writer)).Add(db, [], (typeof(Writer), [("Id", 10)])), Table(db, 0, typeof(Book)));

        var writers = SqlProjection.ToList(SqlQuery.Select(
            Include(db.Books, nameof(Book.Writer), nameof(Writer.Books)), [], ["Writer"], static (_, _, entities) => entities[0]));

        Assert.Single(writers);
    }

    private FakeLibraryContext Context(QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll) => new(_connection, tracking);

    private static IQueryable<(Book Book, Writer? Writer)> BookAndWriter(IQueryable<Book> books)
        => SqlQuery.Select(books, [], ["", "Writer"], static (_, _, entities) => ((Book)entities[0]!, (Writer?)entities[1]));

    private static IQueryable<T> Include<T>(IQueryable<T> source, params string[] path)
        where T : class
    {
        var query = SqlQuery.ToSqlQueryable(source);
        return query.WithTail(new IncludeNode(query.Tail, path));
    }

    private void Queue(params DataTable[] tables)
    {
        foreach (var table in tables)
        {
            _connection.RowSets.Enqueue(table);
        }
    }

    private static DataTable BooksAndWriters(DbContext db, params (int Id, int WriterId)[] books)
    {
        var table = Table(db, 0, typeof(Book), typeof(Writer));
        foreach (var (id, writerId) in books)
        {
            table.Add(db, [], (typeof(Book), [("Id", id), ("WriterId", writerId)]), (typeof(Writer), [("Id", writerId), ("Name", "w" + writerId)]));
        }

        return table;
    }

    // The statement selects the projected columns first, then each entity's columns in model order.
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

internal static class ProjectedRows
{
    /// <summary>Adds a row: the column values, then each entity's values by property name, missing ones null.</summary>
    public static DataTable Add(this DataTable table, DbContext db, object[] columns, params (Type Entity, (string Name, object? Value)[] Values)[] entities)
    {
        var values = new List<object>(columns);
        foreach (var (type, named) in entities)
        {
            foreach (var property in ModelBinder.GetEntity(db.Model, type).Properties)
            {
                var match = Array.FindIndex(named, v => v.Name == property.Name);
                values.Add(match >= 0 ? named[match].Value ?? DBNull.Value : DBNull.Value);
            }
        }

        table.Rows.Add([.. values]);
        return table;
    }
}
