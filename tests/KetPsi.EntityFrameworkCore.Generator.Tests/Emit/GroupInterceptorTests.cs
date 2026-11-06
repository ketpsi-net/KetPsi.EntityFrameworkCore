using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class GroupInterceptorTests : IDisposable
{
    private const string BlogColumns = "[t0].[Id], [t0].[Name], [t0].[Price], [t0].[Status], [t0].[Views]";

    // Compiled, generated and loaded once: every test runs the same interceptors and readers.
    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("GroupInterceptors", GroupSources.Source));
        return (run, run.Load());
    });

    private readonly FakeDbConnection _connection = new();
    private readonly DbContext _db;
    private readonly object _queries;

    public GroupInterceptorTests()
    {
        var assembly = Compiled.Value.Assembly;
        _db = (DbContext)Activator.CreateInstance(assembly.GetType("Shop.ShopDb")!, _connection)!;
        _queries = Activator.CreateInstance(assembly.GetType("Shop.Queries")!, _db)!;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Interceptors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(InterceptorEmitter.HintName), "GroupInterceptors");

    [Fact]
    public void Interceptors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void KeyAndCount_GroupsByTheKey()
    {
        _connection.Rows = Table(["Status", "c"], [0, 2], [1, 3]);

        var rows = AsList(Invoke("KeyAndCount"));

        Assert.Equal("SELECT [t0].[Status], COUNT(*) FROM [Blogs] AS [t0] GROUP BY [t0].[Status]", _connection.LastCommand.CommandText);
        Assert.Equal(["Draft", "Published"], rows.Select(r => Get(r, "Key")!.ToString()));
        Assert.Equal([2, 3], rows.Select(r => Get(r, "Count")));
    }

    [Fact]
    public void CompositeKey_IsRebuiltAndReadByPart()
    {
        _connection.Rows = Table(["Status", "Name", "c"], [1, "alpha", 4L]);

        var row = Assert.Single(AsList(Invoke("Composite")));

        Assert.Equal(
            "SELECT [t0].[Status], [t0].[Name], COUNT_BIG(*) FROM [Blogs] AS [t0] GROUP BY [t0].[Status], [t0].[Name]",
            _connection.LastCommand.CommandText);
        Assert.Equal("alpha", Get(Get(row, "Key")!, "Name"));
        Assert.Equal("Published", Get(Get(row, "Key")!, "Status")!.ToString());
        Assert.Equal("alpha", Get(row, "Name"));
        Assert.Equal(4L, Get(row, "Count"));
    }

    [Fact]
    public void TupleKey_IsRebuiltAndReadByPart()
    {
        _connection.Rows = Table(["Status", "Name"], [0, "beta"]);

        var row = Assert.Single(AsList(Invoke("Tuple")));

        Assert.Equal("beta", Get(row, "Item2"));
        Assert.Equal("(Draft, beta)", Get(row, "Key")!.ToString());
    }

    [Fact]
    public void Aggregates_ReadTheirTypes()
    {
        _connection.Rows = Table(["s", "a", "mi", "ma", "p"], [6, 2.5, "a", DBNull.Value, 12.5m]);

        var row = Assert.Single(AsList(Invoke("Aggregates")));

        Assert.Equal(
            "SELECT COALESCE(SUM([t0].[Id]), 0), AVG(CAST([t0].[Id] AS float)), MIN([t0].[Name]), MAX([t0].[Views]), COALESCE(SUM([t0].[Price]), 0.0) " +
            "FROM [Blogs] AS [t0] GROUP BY [t0].[Status]",
            _connection.LastCommand.CommandText);
        Assert.Equal(6, Get(row, "Sum"));
        Assert.Equal(2.5, Get(row, "Average"));
        Assert.Equal("a", Get(row, "Min"));
        Assert.Null(Get(row, "Max"));
        Assert.Equal(12.5m, Get(row, "Price"));
    }

    [Fact]
    public void Counts_WithPredicates_PassTheirValues()
    {
        _connection.Rows = Table(["a", "n", "d"], [1, 2L, 3]);

        var row = Assert.Single(AsList(Invoke("Counts", 7)));

        Assert.Equal(
            "SELECT COUNT(CASE WHEN [t0].[Id] > @p0 THEN 1 END), COUNT_BIG(CASE WHEN [t0].[Name] <> @p1 THEN 1 END), COUNT(DISTINCT ([t0].[Name])) " +
            "FROM [Blogs] AS [t0] GROUP BY [t0].[Status]",
            _connection.LastCommand.CommandText);
        Assert.Equal([7, ""], Parameters());
        Assert.Equal([1, 2L, 3], new[] { Get(row, "Above"), Get(row, "Named"), Get(row, "Names") });
    }

    [Fact]
    public void WhereOnGroups_IsHaving()
    {
        _connection.Rows = Table(["Views"], [5]);

        var keys = (List<int?>)Invoke("Having", 1)!;

        Assert.Equal(
            "SELECT [t0].[Views] FROM [Blogs] AS [t0] GROUP BY [t0].[Views] HAVING (COUNT(*) > @p0 AND [t0].[Views] IS NOT NULL)",
            _connection.LastCommand.CommandText);
        Assert.Equal([1], Parameters());
        Assert.Equal([5], keys);
    }

    [Fact]
    public void WhereAndOrderAfterSelect_AreHavingAndOrderBy()
    {
        _connection.Rows = Table(["Status", "c"], [1, 3]);

        var row = Assert.Single(AsList(Invoke("WhereAndOrderAfterSelect", 2)));

        Assert.Equal(
            "SELECT [t0].[Status], COUNT(*) FROM [Blogs] AS [t0] GROUP BY [t0].[Status] HAVING COUNT(*) >= @p0 ORDER BY COUNT(*) DESC, [t0].[Status]",
            _connection.LastCommand.CommandText);
        Assert.Equal([2], Parameters());
        Assert.Equal(3, Get(row, "Count"));
    }

    [Fact]
    public void OrderOnGroups_ThenTake_PagesTheOrderedGroups()
    {
        _connection.Rows = Table(["Views"], [DBNull.Value]);

        var keys = AsList(Invoke("OrderedOnGroups"));

        Assert.Equal(
            "SELECT TOP(@p0) [t0].[Views] FROM [Blogs] AS [t0] GROUP BY [t0].[Views] ORDER BY COALESCE(SUM([t0].[Price]), 0.0) DESC",
            _connection.LastCommand.CommandText);
        Assert.Null(Assert.Single(keys));
    }

    [Fact]
    public void ComputedMembers_AreComputedFromTheValues()
    {
        _connection.Rows = Table(["Status", "c"], [0, 2]);

        var row = Assert.Single(AsList(Invoke("Computed", "!")));

        Assert.Equal("Draft!", Get(row, "Label"));
        Assert.Equal(4, Get(row, "Double"));
    }

    [Fact]
    public void ResultSelector_SelectsFromTheGroups()
    {
        _connection.Rows = Table(["Status", "s"], [1, 9]);

        var row = Assert.Single(AsList(Invoke("ResultSelector")));

        Assert.Equal("SELECT [t0].[Status], COALESCE(SUM([t0].[Id]), 0) FROM [Blogs] AS [t0] GROUP BY [t0].[Status]", _connection.LastCommand.CommandText);
        Assert.Equal("Published", Get(row, "key")!.ToString());
        Assert.Equal(9, Get(row, "Total"));
    }

    [Fact]
    public void KeyThroughNavigation_IsJoined()
    {
        _connection.Rows = Table(["Name", "c"], ["alpha", 2]);

        Assert.Single(AsList(Invoke("ThroughNavigation")));
        Assert.Equal(
            "SELECT [t1].[Name], COUNT(*) FROM [Posts] AS [t0] INNER JOIN [Blogs] AS [t1] ON [t0].[BlogId] = [t1].[Id] GROUP BY [t1].[Name]",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task CountAndAny_OfGroups_RunScalarStatements()
    {
        _connection.Scalar = 2;
        Assert.Equal(2, Invoke("CountGroups"));
        Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT [t0].[Status] FROM [Blogs] AS [t0] GROUP BY [t0].[Status]) AS [t1]",
            _connection.LastCommand.CommandText);

        _connection.Scalar = true;
        Assert.Equal(true, await InvokeAsync("AnyGroupAsync", 1, Token));
        Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM [Blogs] AS [t0] GROUP BY [t0].[Status] HAVING COUNT(*) > @p0) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void FirstAndAggregate_OfProjectedGroups()
    {
        _connection.Rows = Table(["Status", "c"], [0, 2]);
        Assert.Equal(2, Get(Invoke("FirstGroup")!, "Count"));
        Assert.Equal(
            "SELECT TOP(1) [t0].[Status], COUNT(*) FROM [Blogs] AS [t0] GROUP BY [t0].[Status] ORDER BY [t0].[Status]",
            _connection.LastCommand.CommandText);

        _connection.Scalar = 3;
        Assert.Equal(3, Invoke("MaxCount"));
        Assert.Equal(
            "SELECT MAX([t1].[c]) FROM (SELECT COUNT(*) AS [c] FROM [Blogs] AS [t0] GROUP BY [t0].[Status]) AS [t1]",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void Groups_AreReadOrderedByTheKey()
    {
        _connection.Rows = Table(["Status", "Id", "Name", "Price", "Status", "Views"], [0, 1, "a", 1m, 0, DBNull.Value], [0, 2, "b", 2m, 0, 3], [1, 3, "c", 3m, 1, DBNull.Value]);

        var groups = AsList(Invoke("Groups"));

        Assert.Equal($"SELECT [t0].[Status], {BlogColumns} FROM [Blogs] AS [t0] ORDER BY [t0].[Status]", _connection.LastCommand.CommandText);
        Assert.Equal(["Draft", "Published"], groups.Select(g => Get(g, "Key")!.ToString()));
        Assert.Equal([2, 1], groups.Select(g => ((IEnumerable)g).Cast<object>().Count()));
    }

    [Fact]
    public async Task GroupsWithAnElementSelector_ReadOnlyTheElement()
    {
        _connection.Rows = Table(["Status", "Name"], [1, "x"], [1, "y"]);

        var groups = AsList(await InvokeAsync("NamesByStatusAsync", Token));

        Assert.Equal("SELECT [t0].[Status], [t0].[Name] FROM [Blogs] AS [t0] ORDER BY [t0].[Status]", _connection.LastCommand.CommandText);
        Assert.Equal(["x", "y"], ((IEnumerable)Assert.Single(groups)).Cast<object>());
    }

    [Fact]
    public void GroupsWithACompositeKey_CompareKeysByValue()
    {
        _connection.Rows = Table(["Status", "Views", "Id", "Name", "Price", "Status", "Views"], [0, 1, 1, "a", 1m, 0, 1], [0, 1, 2, "b", 1m, 0, 1]);

        var group = Assert.Single(AsList(Invoke("CompositeGroups")));

        Assert.Equal(2, ((IEnumerable)group).Cast<object>().Count());
    }

    private object?[] Parameters() => [.. _connection.LastCommand.Parameters.Cast<DbParameter>().Select(static p => p.Value)];

    private static DataTable Table(string[] columns, params object[][] rows)
    {
        var table = new DataTable();
        for (var i = 0; i < columns.Length; i++)
        {
            // A statement can select a column twice; the reader reads by ordinal.
            table.Columns.Add(columns[i] + i.ToString(System.Globalization.CultureInfo.InvariantCulture), typeof(object)).AllowDBNull = true;
        }

        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        return table;
    }

    private async Task<object?> InvokeAsync(string method, params object?[] arguments)
    {
        var task = (Task)Invoke(method, arguments)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private object? Invoke(string method, params object?[] arguments)
        => _queries.GetType().GetMethod(method)!.Invoke(_queries, arguments);

    private static List<object> AsList(object? value) => [.. ((IEnumerable)value!).Cast<object>()];

    private static object? Get(object item, string property) => item.GetType().GetProperty(property)!.GetValue(item);
}
