using System.Data;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class ProjectionTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public ProjectionTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ToList_BuildsAnonymousResultsWithTheReader()
    {
        _connection.Rows = Table(("alpha", 1), ("beta", 2));

        var items = SqlProjection.ToList(
            Project(static (reader, ordinals, _) => new { Name = reader.GetString(ordinals[0]), Id = reader.GetInt32(ordinals[1]) }, nameof(Item.Name), nameof(Item.Id)));

        Assert.Equal([new { Name = "alpha", Id = 1 }, new { Name = "beta", Id = 2 }], items);
        Assert.Equal("SELECT [t0].[Name], [t0].[Id] FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public void Projection_IsNeverTracked()
    {
        _connection.Rows = Table(("alpha", 1));

        SqlProjection.ToList(NameAndId());

        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ToListAsync_AndToArray_ReadEveryRow()
    {
        _connection.Rows = Table(("alpha", 1), ("beta", 2));

        Assert.Equal(2, (await SqlProjection.ToListAsync(NameAndId(), Token)).Count);
        Assert.Equal(2, SqlProjection.ToArray(NameAndId()).Length);
        Assert.Equal(2, (await SqlProjection.ToArrayAsync(NameAndId(), Token)).Length);
    }

    [Fact]
    public void First_UsesARowLimit()
    {
        _connection.Rows = Table(("alpha", 1));

        Assert.Equal(new NameId("alpha", 1), SqlProjection.First(NameAndId()));
        Assert.StartsWith("SELECT TOP(1) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void First_NoRows_ThrowsLinqMessage()
    {
        _connection.Rows = Table();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlProjection.First(NameAndId()));

        Assert.Equal("Sequence contains no elements", exception.Message);
    }

    [Fact]
    public async Task FirstAsync_NoRows_ThrowsEfCoreMessage()
    {
        _connection.Rows = Table();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SqlProjection.FirstAsync(NameAndId(), Token));

        Assert.Equal("Sequence contains no elements.", exception.Message);
    }

    [Fact]
    public void FirstOrDefault_ValueType_ReturnsDefault()
    {
        _connection.Rows = IdTable();

        Assert.Equal(0, SqlProjection.FirstOrDefault(Ids()));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsTheFirstValue()
    {
        _connection.Rows = IdTable(7, 8);

        Assert.Equal(7, await SqlProjection.FirstOrDefaultAsync(Ids(), Token));
    }

    [Fact]
    public async Task Last_ReadsTheReversedEntityOrdering()
    {
        IQueryable<int> OrderedIds() => SqlQuery.Select(SqlQuery.OrderBy(_db.Items, nameof(Item.Name)), [nameof(Item.Id)], [], ReadId);
        _connection.Rows = IdTable(8);

        Assert.Equal(8, SqlProjection.Last(OrderedIds()));
        Assert.Equal("SELECT TOP(1) [t0].[Id] FROM [shop].[Items] AS [t0] ORDER BY [t0].[Name] DESC", _connection.LastCommand.CommandText);
        Assert.Equal(8, await SqlProjection.LastAsync(OrderedIds(), Token));
        Assert.Equal(8, await SqlProjection.LastOrDefaultAsync(OrderedIds(), Token));

        _connection.Rows = IdTable();
        Assert.Equal(0, SqlProjection.LastOrDefault(OrderedIds()));
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => SqlProjection.LastAsync(OrderedIds(), Token));
        Assert.Equal("Sequence contains no elements.", none.Message);
    }

    [Fact]
    public void Last_WithoutOrdering_Throws()
        => Assert.StartsWith(
            "Queries performing 'Last' operation must have a deterministic sort order.",
            Assert.Throws<InvalidOperationException>(() => SqlProjection.Last(Ids())).Message,
            StringComparison.Ordinal);

    [Fact]
    public void Single_TwoRows_Throws()
    {
        _connection.Rows = IdTable(1, 2);

        Assert.Equal("Sequence contains more than one element", Assert.Throws<InvalidOperationException>(() => SqlProjection.Single(Ids())).Message);
        Assert.StartsWith("SELECT TOP(2) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleVariants_ReturnTheOnlyValue()
    {
        _connection.Rows = IdTable(5);

        Assert.Equal(5, SqlProjection.Single(Ids()));
        Assert.Equal(5, await SqlProjection.SingleAsync(Ids(), Token));
        Assert.Equal(5, SqlProjection.SingleOrDefault(Ids()));
        Assert.Equal(5, await SqlProjection.SingleOrDefaultAsync(Ids(), Token));
    }

    [Fact]
    public async Task ScalarTerminals_RunScalarStatements()
    {
        _connection.Scalar = 3;

        Assert.Equal(3, SqlProjection.Count(Ids()));
        Assert.Equal(3, await SqlProjection.CountAsync(Ids(), Token));
        Assert.Equal(3L, SqlProjection.LongCount(Ids()));
        Assert.Equal(3L, await SqlProjection.LongCountAsync(Ids(), Token));
        Assert.Equal("SELECT COUNT_BIG(*) FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);

        _connection.Scalar = true;
        Assert.True(SqlProjection.Any(Ids()));
        Assert.True(await SqlProjection.AnyAsync(Ids(), Token));
    }

    [Fact]
    public void Distinct_RunsDistinctStatement()
    {
        _connection.Rows = IdTable(1, 2);

        Assert.Equal([1, 2], SqlProjection.ToList(SqlProjection.Distinct(Ids())));
        Assert.Equal("SELECT DISTINCT [t0].[Id] FROM [shop].[Items] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public void SkipAndTake_ChainOnTheProjection()
    {
        _connection.Rows = IdTable(3);

        SqlProjection.ToList(SqlProjection.Take(SqlProjection.Skip(SqlQuery.Select(SqlQuery.OrderBy(_db.Items, nameof(Item.Id)), [nameof(Item.Id)], [], ReadId), 2), 1));

        Assert.Equal(
            "SELECT [t0].[Id] FROM [shop].[Items] AS [t0] ORDER BY [t0].[Id] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void Enumerating_ExecutesTheQuery()
    {
        _connection.Rows = IdTable(1, 2);

        var ids = new List<int>();
        foreach (var id in Ids())
        {
            ids.Add(id);
        }

        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public async Task EnumeratingAsync_ExecutesTheQuery()
    {
        _connection.Rows = IdTable(1, 2);

        var ids = new List<int>();
        await foreach (var id in ((IAsyncEnumerable<int>)Ids()).WithCancellation(Token))
        {
            ids.Add(id);
        }

        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public void Ordinals_MapRepeatedNamesToTheSameColumn()
    {
        var projection = new ProjectionNode(new RootNode(typeof(Item)), [nameof(Item.Name), nameof(Item.Id), nameof(Item.Name)], []);

        Assert.Equal([nameof(Item.Name), nameof(Item.Id)], projection.Columns);
        Assert.Equal([0, 1, 0], ProjectionRunner.Ordinals(projection));
    }

    [Fact]
    public void IsInterceptable_OnlyForProjectedQueries()
    {
        Assert.True(SqlProjection.IsInterceptable(Ids()));
        Assert.False(SqlProjection.IsInterceptable(new[] { 1, 2 }.AsQueryable()));
    }

    [Fact]
    public void OperatorsOnAnotherQuery_Throw()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SqlProjection.ToList(new[] { 1 }.AsQueryable()));

        Assert.Contains("Select", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_WithoutPropertiesOrEntities_Throws()
        => Assert.Throws<ArgumentException>(() => SqlQuery.Select(_db.Items, [], [], ReadId));

    [Fact]
    public void Select_ReturnsAProjectedQuery()
        => Assert.IsType<ProjectedQueryable<int>>(Ids());

    private static int ReadId(System.Data.Common.DbDataReader reader, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities)
        => reader.GetInt32(ordinals[0]);

    private IQueryable<int> Ids() => SqlQuery.Select(_db.Items, [nameof(Item.Id)], [], ReadId);

    private IQueryable<NameId> NameAndId()
        => SqlQuery.Select(_db.Items, [nameof(Item.Name), nameof(Item.Id)], [], static (reader, ordinals, _) => new NameId(reader.GetString(ordinals[0]), reader.GetInt32(ordinals[1])));

    // The projected type is inferred from the reader, as in generated code, so it can be an anonymous type.
    private IQueryable<T> Project<T>(ProjectionReader<T> reader, params string[] propertyNames) => SqlQuery.Select(_db.Items, propertyNames, [], reader);

    private static DataTable Table(params (string Name, int Id)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Id", typeof(int));
        foreach (var (name, id) in rows)
        {
            table.Rows.Add(name, id);
        }

        return table;
    }

    private static DataTable IdTable(params int[] ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
    }

    private sealed record NameId(string Name, int Id);
}
