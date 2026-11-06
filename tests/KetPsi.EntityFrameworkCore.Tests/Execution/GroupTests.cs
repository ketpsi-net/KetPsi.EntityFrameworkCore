using System.Data;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class GroupTests : IDisposable
{
    private static readonly GroupValue Key0 = GroupValue.Key(0);
    private static readonly GroupValue CountAll = GroupValue.Count(isLong: false);

    private readonly FakeDbConnection _connection = new();
    private readonly FakeItemContext _db;

    public GroupTests()
    {
        FakeItemContext.EnsureRegistered();
        _db = new FakeItemContext(_connection);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ToList_ReadsEachGroupsValues()
    {
        _connection.Rows = Table((null, 2), (5, 1));

        var groups = SqlProjection.ToList(KeyAndCount());

        Assert.Equal([new KeyCount(null, 2), new KeyCount(5, 1)], groups);
        Assert.Equal("SELECT [t0].[Score], COUNT(*) FROM [shop].[Items] AS [t0] GROUP BY [t0].[Score]", _connection.LastCommand.CommandText);
        Assert.Equal(ConnectionState.Closed, _connection.State);
    }

    [Fact]
    public async Task ToListAsync_AndEnumeration_ReadEveryGroup()
    {
        _connection.Rows = Table((1, 2), (5, 1));

        Assert.Equal(2, (await SqlProjection.ToListAsync(KeyAndCount(), Token)).Count);
        Assert.Equal(2, KeyAndCount().AsEnumerable().Count());

        var count = 0;
        await foreach (var _ in (IAsyncEnumerable<KeyCount>)KeyAndCount())
        {
            count++;
        }

        Assert.Equal(2, count);
    }

    [Fact]
    public void First_ReadsOneGroup()
    {
        _connection.Rows = Table((1, 2));

        Assert.Equal(new KeyCount(1, 2), SqlProjection.First(SqlProjection.OrderBy(KeyAndCount(), Key0, [])));
        Assert.StartsWith("SELECT TOP(1) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Having_PassesItsValuesAsParameters()
    {
        _connection.Rows = Table((1, 3));
        var filtered = SqlProjection.Where(KeyAndCount(), new ComparisonTemplate("0", ComparisonOperator.GreaterThan, 0), [CountAll], [2]);

        SqlProjection.ToList(filtered);

        var parameter = Assert.Single(_connection.LastCommand.Parameters.Cast<System.Data.Common.DbParameter>());
        Assert.Equal(2, parameter.Value);
        Assert.Equal(DbType.Int32, parameter.DbType);
    }

    [Fact]
    public async Task CountAndAny_OfGroups_RunScalarStatements()
    {
        _connection.Scalar = 4;
        Assert.Equal(4, SqlGrouping.Count(ByScore()));
        Assert.Equal(4, await SqlGrouping.CountAsync(ByScore(), Token));
        Assert.Equal(4L, SqlGrouping.LongCount(ByScore()));
        Assert.Equal(4L, await SqlGrouping.LongCountAsync(ByScore(), Token));
        Assert.Equal(
            "SELECT COUNT_BIG(*) FROM (SELECT [t0].[Score] FROM [shop].[Items] AS [t0] GROUP BY [t0].[Score]) AS [t1]",
            _connection.LastCommand.CommandText);

        _connection.Scalar = true;
        Assert.True(SqlGrouping.Any(ByScore()));
        Assert.True(await SqlGrouping.AnyAsync(ByScore(), Token));
    }

    [Fact]
    public void AggregateOfGroups_ReadsTheScalar()
    {
        _connection.Scalar = 3;
        var counts = SqlGrouping.Select(ByScore(), [CountAll], [], static (reader, ordinals, _) => reader.GetInt32(ordinals[0]));

        Assert.Equal(3, SqlProjection.Aggregate<int, int>(counts, AggregateFunction.Max, AggregateValueType.Column));
    }

    [Fact]
    public void ToList_OfGroups_GroupsTheRowsByKey()
    {
        _connection.Rows = NameTable((null, "a"), (null, "b"), (5, "c"));

        var groups = SqlGrouping.ToList(NamesByScore());

        Assert.Equal([null, 5], groups.Select(static g => g.Key));
        Assert.Equal(["a", "b"], groups[0]);
        Assert.Equal(["c"], groups[1]);
        Assert.Equal("SELECT [t0].[Score], [t0].[Name] FROM [shop].[Items] AS [t0] ORDER BY [t0].[Score]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public void Groups_StartWhereTheKeyChanges_AsInEfCore()
    {
        _connection.Rows = NameTable((1, "a"), (2, "b"), (1, "c"));

        var groups = SqlGrouping.ToArray(NamesByScore());

        Assert.Equal([1, 2, 1], groups.Select(static g => g.Key));
    }

    [Fact]
    public async Task Groups_AsyncAndEnumerated_ReadTheSameGroups()
    {
        _connection.Rows = NameTable((1, "a"), (1, "b"), (2, "c"));

        Assert.Equal(2, (await SqlGrouping.ToListAsync(NamesByScore(), Token)).Count);
        Assert.Equal(2, (await SqlGrouping.ToArrayAsync(NamesByScore(), Token)).Length);
        Assert.Equal(2, NamesByScore().AsEnumerable().Count());

        var elements = 0;
        await foreach (var group in (IAsyncEnumerable<IGrouping<int?, string>>)NamesByScore())
        {
            elements += group.Count();
        }

        Assert.Equal(3, elements);
    }

    [Fact]
    public void Groups_OfNoRows_AreEmpty()
    {
        _connection.Rows = NameTable();

        Assert.Empty(SqlGrouping.ToList(NamesByScore()));
    }

    [Fact]
    public void Groups_AfterWhereOnGroups_AreNotSupported()
        => Assert.Throws<NotSupportedException>(() => SqlGrouping.ToList(SqlGrouping.Where(
            NamesByScore(), new ComparisonTemplate("0", ComparisonOperator.GreaterThan, 0), [CountAll], [1])));

    [Fact]
    public void OperatorsOnOtherQueries_Throw()
    {
        var groups = Array.Empty<Item>().AsQueryable().GroupBy(static i => i.Score);

        Assert.Throws<InvalidOperationException>(() => SqlGrouping.Count(groups));
        Assert.Throws<InvalidOperationException>(() => SqlGrouping.OrderBy(groups, Key0, []));
    }

    private IQueryable<IGrouping<int?, Item>> ByScore()
        => SqlGrouping.GroupBy<Item, int?, Item>(_db.Items, [nameof(Item.Score)], ReadScore, [], [""], static (_, _, entities) => (Item)entities[0]!);

    // GroupBy(i => i.Score, i => i.Name)
    private IQueryable<IGrouping<int?, string>> NamesByScore()
        => SqlGrouping.GroupBy<Item, int?, string>(
            _db.Items, [nameof(Item.Score)], ReadScore, [nameof(Item.Name)], [], static (reader, ordinals, _) => reader.GetString(ordinals[0]));

    private static int? ReadScore(System.Data.Common.DbDataReader reader, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities)
        => reader.IsDBNull(ordinals[0]) ? null : reader.GetInt32(ordinals[0]);

    private static DataTable NameTable(params (int? Score, string Name)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Score", typeof(int));
        table.Columns.Add("Name", typeof(string));
        foreach (var (score, name) in rows)
        {
            table.Rows.Add(score is { } value ? value : DBNull.Value, name);
        }

        return table;
    }

    private IQueryable<KeyCount> KeyAndCount()
        => SqlGrouping.Select(
            ByScore(),
            [Key0, CountAll],
            [],
            static (reader, ordinals, _) => new KeyCount(reader.IsDBNull(ordinals[0]) ? null : reader.GetInt32(ordinals[0]), reader.GetInt32(ordinals[1])));

    private static DataTable Table(params (int? Score, int Count)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Score", typeof(int));
        table.Columns.Add("c", typeof(int));
        foreach (var (score, count) in rows)
        {
            table.Rows.Add(score is { } value ? value : DBNull.Value, count);
        }

        return table;
    }

    private sealed record KeyCount(int? Key, int Count);
}
