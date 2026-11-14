using System.Collections;
using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary>
/// Collections a projection selects: each is loaded with one more statement by the keys of the rows' parents, and the
/// results are built once they are loaded.
/// </summary>
public sealed class ProjectedCollectionTests
{
    private readonly FakeDbConnection _connection = new();

    public ProjectedCollectionTests()
    {
        FakeLibraryContext.EnsureRegistered();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void EntityElements_AreLoadedByTheParentKeys_InKeyOrder()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Writers(("w10", 10), ("w20", 20)), BookRows(db, (2, 10), (1, 10)));

        var rows = SqlProjection.ToList(NamesAndBooks(db));

        Assert.Equal(["w10", "w20"], rows.Select(r => r.Name));
        Assert.Equal([2, 1], rows[0].Books.Select(b => b.Id));
        Assert.Empty(rows[1].Books);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(2, _connection.Commands.Count);
        Assert.Equal("SELECT [t0].[Name], [t0].[Id] FROM [Writers] AS [t0]", _connection.Commands[0].CommandText);
        Assert.Equal(
            "SELECT [t0].[WriterId], [t0].[Id], [t0].[EditorId], [t0].[WriterId] FROM [Books] AS [t0] " +
            "WHERE [t0].[WriterId] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$')) ORDER BY [t0].[Id]",
            _connection.Commands[1].CommandText);
        Assert.Equal("[10,20]", _connection.Commands[1].Parameters[0].Value);
    }

    [Fact]
    public void ProjectedElements_AreFilteredOrderedAndConverted()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var ids = Table(2);
        ids.Rows.Add(3, 10);
        ids.Rows.Add(2, 10);
        Queue(Writers(("w10", 10)), ids);

        var collection = new CollectionProjection<int>(
            nameof(Writer.Books),
            new ComparisonTemplate(nameof(Book.Id), ComparisonOperator.GreaterThan, 0),
            [1],
            [new CollectionOrdering(nameof(Book.Id), Descending: true)],
            [nameof(Book.Id)],
            [],
            static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));
        var query = SqlQuery.Select<Writer, WriterIds>(
            db.Writers, [nameof(Writer.Name)], [], [], [], [collection],
            static (reader, ordinals, entities) =>
            {
                var name = reader.GetString(ordinals[0]);
                var items = (List<int>)entities[0]!;
                return () => new WriterIds(name, items.ToArray());
            });

        var row = Assert.Single(SqlProjection.ToList(query));

        Assert.Equal([3, 2], row.Ids);
        Assert.Equal(
            "SELECT [t0].[Id], [t0].[WriterId] FROM [Books] AS [t0] " +
            "WHERE [t0].[WriterId] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$')) AND [t0].[Id] > @p1 ORDER BY [t0].[Id] DESC",
            _connection.Commands[1].CommandText);
        Assert.Equal(1, _connection.Commands[1].Parameters[1].Value);
    }

    [Fact]
    public void RowsWithoutParents_SendNoSecondStatement()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Writers());

        Assert.Empty(SqlProjection.ToList(NamesAndBooks(db)));
        Assert.Single(_connection.Commands);
    }

    [Fact]
    public void Tracking_AttachesTheElements_AndFixesUpTheProjectedParent()
    {
        using var db = Context();
        // The parent key the collection matches, then the writer's columns.
        var writers = Table(3);
        writers.Rows.Add(10, 10, "w10");
        Queue(writers, BookRows(db, (1, 10)));

        var query = SqlQuery.Select<Writer, WriterAndBooks>(
            db.Writers, [], [""], [], [], [BooksOf()],
            static (_, _, entities) =>
            {
                var writer = (Writer)entities[0]!;
                var books = (List<Book>)entities[1]!;
                return () => new WriterAndBooks(writer, books);
            });

        var row = Assert.Single(SqlProjection.ToList(query));

        Assert.Same(row.Writer, Assert.Single(row.Books).Writer);
        Assert.Equal([1], row.Writer.Books!.Select(b => b.Id));
        Assert.Equal(2, db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public async Task Async_FirstLimitsTheParents_AndLoadsTheirCollections()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Writers(("w10", 10)), BookRows(db, (1, 10)));

        var row = await SqlProjection.FirstAsync(NamesAndBooks(db), Token);

        Assert.Equal([1], row.Books.Select(b => b.Id));
        Assert.StartsWith("SELECT TOP(1) [t0].[Name], [t0].[Id] FROM", _connection.Commands[0].CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enumeration_ReadsEveryRowFirst()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        Queue(Writers(("w10", 10)), BookRows(db, (1, 10)));

        var names = new List<string>();
        await foreach (var row in (IAsyncEnumerable<WriterBooks>)NamesAndBooks(db))
        {
            names.Add(row.Name + ":" + row.Books.Count);
        }

        Assert.Equal(["w10:1"], names);
    }

    [Fact]
    public void Count_LeavesTheCollectionsOut()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        _connection.Scalar = 2;

        Assert.Equal(2, SqlProjection.Count(NamesAndBooks(db)));
        Assert.Equal("SELECT COUNT(*) FROM [Writers] AS [t0]", Assert.Single(_connection.Commands).CommandText);
    }

    [Fact]
    public void Distinct_Throws_AsInEfCore()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);

        Assert.Throws<InvalidOperationException>(() => SqlProjection.ToList(SqlProjection.Distinct(NamesAndBooks(db))));
    }

    [Fact]
    public void ShadowForeignKey_MatchesTheParents()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var books = Table(2);
        books.Rows.Add(10);
        var reviews = Table(2);
        reviews.Rows.Add(5, 10);
        Queue(books, reviews);

        var collection = new CollectionProjection<int>(
            nameof(Book.Reviews), null, [], [], [nameof(Review.Id)], [], static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));
        var query = SqlQuery.Select<Book, List<int>>(
            db.Books, [], [], [], [], [collection],
            static (_, _, entities) =>
            {
                var items = (List<int>)entities[0]!;
                return () => items;
            });

        Assert.Equal([5], Assert.Single(SqlProjection.ToList(query)));
        Assert.Equal("SELECT [t0].[Id] FROM [Books] AS [t0]", _connection.Commands[0].CommandText);
        Assert.Contains("[t0].[BookId] IN", _connection.Commands[1].CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionThroughAReference_MatchesTheReferencedKey_AndANullKeyHasNoElements()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var books = Table(1);
        books.Rows.Add(7);
        books.Rows.Add(DBNull.Value);
        var others = Table(2);
        others.Rows.Add(3, 7);
        Queue(books, others);

        var collection = new CollectionProjection<int>(
            "Writer.Books", null, [], [], [nameof(Book.Id)], [], static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));
        var query = SqlQuery.Select<Book, List<int>>(
            db.Books, [], [], [], [], [collection],
            static (_, _, entities) =>
            {
                var items = (List<int>)entities[0]!;
                return () => items;
            });

        var rows = SqlProjection.ToList(query);

        Assert.Equal([3], rows[0]);
        Assert.Empty(rows[1]);
        Assert.Equal(
            "SELECT [t1].[Id] FROM [Books] AS [t0] INNER JOIN [Writers] AS [t1] ON [t0].[WriterId] = [t1].[Id]",
            _connection.Commands[0].CommandText);
        Assert.Contains("[t0].[WriterId] IN", _connection.Commands[1].CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyToManyCollection_IsNotSupported()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var collection = new CollectionProjection<int>(
            nameof(Book.Tags), null, [], [], [nameof(Tag.Id)], [], static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));

        Assert.Throws<NotSupportedException>(() => SqlQuery.Select<Book, int>(
            db.Books, [], [], [], [], [collection], static (_, _, _) => () => 0));
    }

    [Fact]
    public void ReferenceAsACollection_Throws()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);
        var collection = new CollectionProjection<int>(
            nameof(Book.Writer), null, [], [], [nameof(Writer.Id)], [], static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));

        Assert.Throws<InvalidOperationException>(() => SqlQuery.Select<Book, int>(
            db.Books, [], [], [], [], [collection], static (_, _, _) => () => 0));
    }

    [Fact]
    public void Arguments_AreChecked()
    {
        using var db = Context(QueryTrackingBehavior.NoTracking);

        Assert.Throws<ArgumentException>(() => SqlQuery.Select<Writer, int>(db.Writers, [], [], [], [], [], static (_, _, _) => () => 0));
        Assert.Throws<ArgumentException>(() => new CollectionProjection<int>("Books", null, [], [], [], [], static (_, _, _) => 0));
        Assert.Throws<ArgumentException>(() => new CollectionProjection<int>("", null, [], [], ["Id"], [], static (_, _, _) => 0));
    }

    public sealed record WriterBooks(string Name, List<Book> Books);

    public sealed record WriterIds(string Name, int[] Ids);

    public sealed record WriterAndBooks(Writer Writer, List<Book> Books);

    private static CollectionProjection<Book> BooksOf()
        => new(nameof(Writer.Books), null, [], [], [], [""], static (_, _, entities) => (Book)entities[0]!);

    private static IQueryable<WriterBooks> NamesAndBooks(FakeLibraryContext db)
        => SqlQuery.Select<Writer, WriterBooks>(
            db.Writers, [nameof(Writer.Name)], [], [], [], [BooksOf()],
            static (reader, ordinals, entities) =>
            {
                var name = reader.GetString(ordinals[0]);
                var books = (List<Book>)entities[0]!;
                return () => new WriterBooks(name, books);
            });

    private FakeLibraryContext Context(QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll) => new(_connection, tracking);

    private void Queue(params DataTable[] tables)
    {
        foreach (var table in tables)
        {
            _connection.RowSets.Enqueue(table);
        }
    }

    private static IDictionary Values(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value);

    private static DataTable Table(int columns)
    {
        var table = new DataTable();
        for (var i = 0; i < columns; i++)
        {
            table.Columns.Add("c" + i, typeof(object)).AllowDBNull = true;
        }

        return table;
    }

    // The writers' rows of NamesAndBooks: the name, then the key.
    private static DataTable Writers(params (string Name, int Id)[] writers)
    {
        var table = Table(2);
        foreach (var (name, id) in writers)
        {
            table.Rows.Add(name, id);
        }

        return table;
    }

    // The books' rows of an entity element: the foreign key, then the book's columns.
    private static DataTable BookRows(DbContext db, params (int Id, int WriterId)[] books)
    {
        var table = Table(1);
        foreach (var column in LibraryRows.Table(db, typeof(Book)).Columns.Cast<DataColumn>())
        {
            table.Columns.Add("b" + column.ColumnName, typeof(object)).AllowDBNull = true;
        }

        foreach (var (id, writerId) in books)
        {
            var row = LibraryRows.Table(db, typeof(Book)).Row(db, (typeof(Book), Values(("Id", id), ("WriterId", writerId)))).Rows[0].ItemArray;
            table.Rows.Add([writerId, .. row]);
        }

        return table;
    }
}
