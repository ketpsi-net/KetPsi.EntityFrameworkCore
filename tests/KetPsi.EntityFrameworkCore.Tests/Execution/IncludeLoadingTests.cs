using System.Collections;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class IncludeLoadingTests
{
    private readonly FakeDbConnection _connection = new();

    public IncludeLoadingTests()
    {
        FakeLibraryContext.EnsureRegistered();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Tracking_Reference_SharesTrackedInstanceAndMarksLoaded()
    {
        using var db = Context();
        Queue(BooksWithWriters(db, (1, 10), (2, 10)));

        var books = SqlQuery.ToList(Include(db.Books, nameof(Book.Writer)));

        Assert.Same(books[0].Writer, books[1].Writer);
        Assert.Equal(10, books[0].Writer.Id);
        Assert.Equal([1, 2], books[0].Writer.Books!.Select(b => b.Id).Order());
        Assert.All(books, b => Assert.True(db.Entry(b).Reference(x => x.Writer).IsLoaded));
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Single(_connection.Commands);
    }

    [Fact]
    public void Tracking_MissingOptionalReference_IsNullAndLoaded()
    {
        using var db = Context();
        var table = LibraryRows.Table(db, typeof(Book), typeof(Editor))
            .Row(db, (typeof(Book), Values(("Id", 1), ("WriterId", 10))), (typeof(Editor), Values()));
        Queue(table);

        var book = Assert.Single(SqlQuery.ToList(Include(db.Books, nameof(Book.Editor))));

        Assert.Null(book.Editor);
        Assert.True(db.Entry(book).Reference(b => b.Editor).IsLoaded);
    }

    [Fact]
    public void Tracking_Collection_LoadsByParentKeysAndCreatesEmptyCollections()
    {
        using var db = Context();
        Queue(Writers(db, 10, 20), Books(db, (1, 10), (2, 10)));

        var writers = SqlQuery.ToList(Include(db.Writers, nameof(Writer.Books)));

        Assert.Equal([1, 2], writers[0].Books!.Select(b => b.Id).Order());
        Assert.IsType<HashSet<Book>>(writers[1].Books);
        Assert.Empty(writers[1].Books!);
        Assert.All(writers, w => Assert.True(db.Entry(w).Collection(x => x.Books!).IsLoaded));
        Assert.All(writers[0].Books!, b => Assert.Same(writers[0], b.Writer));
        Assert.Equal(2, _connection.Commands.Count);
        Assert.Contains("OPENJSON", _connection.Commands[1].CommandText, StringComparison.Ordinal);
        Assert.Equal("[10,20]", _connection.Commands[1].Parameters[0].Value);
    }

    [Fact]
    public void Tracking_AlreadyTrackedParent_IsNotRefreshedButGetsCollection()
    {
        using var db = Context();
        var tracked = new Writer { Id = 10, Name = "edited" };
        db.Attach(tracked);
        Queue(Writers(db, 10), Books(db, (1, 10)));

        var writer = Assert.Single(SqlQuery.ToList(Include(db.Writers, nameof(Writer.Books))));

        Assert.Same(tracked, writer);
        Assert.Equal("edited", writer.Name);
        Assert.Equal([1], writer.Books!.Select(b => b.Id));
    }

    [Fact]
    public void Collection_WithoutParents_SendsNoSecondStatement()
    {
        using var db = Context();
        Queue(Writers(db));

        Assert.Empty(SqlQuery.ToList(Include(db.Writers, nameof(Writer.Books))));
        Assert.Single(_connection.Commands);
    }

    [Fact]
    public void Tracking_NestedCollections_LoadLevelByLevelWithShadowForeignKeys()
    {
        using var db = Context();
        var reviews = LibraryRows.Table(db, typeof(Review))
            .Row(db, (typeof(Review), Values(("Id", 100), ("BookId", 1))))
            .Row(db, (typeof(Review), Values(("Id", 101), ("BookId", 2))));
        Queue(Writers(db, 10), Books(db, (1, 10), (2, 10)), reviews);

        var writer = Assert.Single(SqlQuery.ToList(Include(db.Writers, nameof(Writer.Books), nameof(Book.Reviews))));

        var books = writer.Books!.OrderBy(b => b.Id).ToList();
        Assert.Equal([100], books[0].Reviews.Select(r => r.Id));
        Assert.Equal([101], books[1].Reviews.Select(r => r.Id));
        Assert.Same(books[0], books[0].Reviews[0].Book);
        Assert.Equal(1, db.Entry(books[0].Reviews[0]).Property("BookId").CurrentValue);
        Assert.Equal(3, _connection.Commands.Count);
        Assert.Equal("[1,2]", _connection.Commands[2].Parameters[0].Value);
    }

    [Fact]
    public void NoTracking_Reference_IsAnInstancePerRow_WithInverseCollection()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(BooksWithWriters(db, (1, 10), (2, 10)));

        var books = SqlQuery.ToList(Include(db.Books, nameof(Book.Writer)));

        Assert.NotSame(books[0].Writer, books[1].Writer);
        Assert.Same(books[0], Assert.Single(books[0].Writer.Books!));
        Assert.Same(books[1], Assert.Single(books[1].Writer.Books!));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void NoTracking_Collection_SetsChildrenAndInverseReference()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Writers(db, 10, 20), Books(db, (1, 10), (2, 10)));

        var writers = SqlQuery.ToList(Include(db.Writers, nameof(Writer.Books)));

        Assert.Equal([1, 2], writers[0].Books!.Select(b => b.Id));
        Assert.All(writers[0].Books!, b => Assert.Same(writers[0], b.Writer));
        Assert.IsType<HashSet<Book>>(writers[1].Books);
        Assert.Empty(writers[1].Books!);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void NoTracking_ListCollection_KeepsInitializedInstance()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var reviews = LibraryRows.Table(db, typeof(Review)).Row(db, (typeof(Review), Values(("Id", 100), ("BookId", 1))));
        Queue(Books(db, (1, 10)), reviews);

        var book = Assert.Single(SqlQuery.ToList(Include(db.Books, nameof(Book.Reviews))));

        Assert.IsType<List<Review>>(book.Reviews);
        Assert.Same(book, Assert.Single(book.Reviews).Book);
    }

    [Fact]
    public void IdentityResolution_SharesInstancesAcrossRows()
    {
        using var db = Context(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        Queue(BooksWithWriters(db, (1, 10), (2, 10)));

        var books = SqlQuery.ToList(Include(db.Books, nameof(Book.Writer)));

        Assert.Same(books[0].Writer, books[1].Writer);
        Assert.Equal([1, 2], books[0].Writer.Books!.Select(b => b.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void IdentityResolution_CollectionUnderSharedReference_LoadsOncePerKey()
    {
        using var db = Context(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        Queue(BooksWithWriters(db, (1, 10), (2, 10)), Books(db, (1, 10), (2, 10)));

        var books = SqlQuery.ToList(Include(db.Books, nameof(Book.Writer), nameof(Writer.Books)));

        Assert.Equal("[10]", _connection.Commands[1].Parameters[0].Value);
        Assert.Same(books[0].Writer, books[1].Writer);
        Assert.Equal([1, 2], books[0].Writer.Books!.Select(b => b.Id).Order());
    }

    [Fact]
    public void Enumerating_LoadsIncludesBeforeReturningRows()
    {
        using var db = Context();
        Queue(Writers(db, 10), Books(db, (1, 10)));

        var writers = new List<Writer>();
        foreach (var writer in SqlQuery.ToSqlQueryable(Include(db.Writers, nameof(Writer.Books))))
        {
            Assert.Single(writer.Books!);
            writers.Add(writer);
        }

        Assert.Single(writers);
    }

    [Fact]
    public async Task AsyncTerminals_LoadIncludes()
    {
        using var db = Context();
        Queue(Writers(db, 10), Books(db, (1, 10)));

        var writer = await SqlQuery.FirstAsync(Include(db.Writers, nameof(Writer.Books)), Token);

        Assert.Single(writer.Books!);
        Assert.StartsWith("SELECT TOP(1)", _connection.Commands[0].CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumeratingAsync_LoadsIncludes()
    {
        using var db = Context();
        Queue(BooksWithWriters(db, (1, 10)));

        await foreach (var book in ((IAsyncEnumerable<Book>)SqlQuery.ToSqlQueryable(Include(db.Books, nameof(Book.Writer)))).WithCancellation(Token))
        {
            Assert.Equal(10, book.Writer.Id);
        }
    }

    [Fact]
    public void Count_IgnoresIncludes()
    {
        using var db = Context();
        _connection.Scalar = 3;

        Assert.Equal(3, SqlQuery.Count(Include(db.Books, nameof(Book.Writer))));
        Assert.DoesNotContain("JOIN", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNavigation_Throws()
    {
        using var db = Context();

        Assert.Throws<InvalidOperationException>(() => SqlQuery.ToList(Include(db.Books, "Missing")));
    }

    private FakeLibraryContext Context(QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll) => new(_connection, tracking);

    private void Queue(params System.Data.DataTable[] tables)
    {
        foreach (var table in tables)
        {
            _connection.RowSets.Enqueue(table);
        }
    }

    private static IQueryable<T> Include<T>(IQueryable<T> source, params string[] path)
        where T : class
    {
        var query = SqlQuery.ToSqlQueryable(source);
        return query.WithTail(new IncludeNode(query.Tail, path));
    }

    private static IDictionary Values(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value);

    private static System.Data.DataTable Writers(DbContext db, params int[] ids)
    {
        var table = LibraryRows.Table(db, typeof(Writer));
        foreach (var id in ids)
        {
            table.Row(db, (typeof(Writer), Values(("Id", id), ("Name", "w" + id))));
        }

        return table;
    }

    private static System.Data.DataTable Books(DbContext db, params (int Id, int WriterId)[] books)
    {
        var table = LibraryRows.Table(db, typeof(Book));
        foreach (var (id, writerId) in books)
        {
            table.Row(db, (typeof(Book), Values(("Id", id), ("WriterId", writerId))));
        }

        return table;
    }

    private static System.Data.DataTable BooksWithWriters(DbContext db, params (int Id, int WriterId)[] books)
    {
        var table = LibraryRows.Table(db, typeof(Book), typeof(Writer));
        foreach (var (id, writerId) in books)
        {
            table.Row(db, (typeof(Book), Values(("Id", id), ("WriterId", writerId))), (typeof(Writer), Values(("Id", writerId), ("Name", "w" + writerId))));
        }

        return table;
    }
}
