using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class JoinInterceptorTests : IDisposable
{
    private const string Join = "FROM [Posts] AS [t0] INNER JOIN [Blogs] AS [t1] ON [t0].[BlogId] = [t1].[Id]";

    // Compiled, generated and loaded once: every test runs the same interceptors and readers.
    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("JoinInterceptors", JoinSources.Source));
        return (run, run.Load());
    });

    private readonly FakeDbConnection _connection = new();
    private readonly DbContext _db;
    private readonly object _queries;

    public JoinInterceptorTests()
    {
        var assembly = Compiled.Value.Assembly;
        _db = (DbContext)Activator.CreateInstance(assembly.GetType("Shop.ShopDb")!, _connection)!;
        _queries = Activator.CreateInstance(assembly.GetType("Shop.Queries")!, _db)!;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Interceptors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(InterceptorEmitter.HintName), "JoinInterceptors");

    [Fact]
    public void Interceptors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void Join_SelectsColumnsOfBothSides()
    {
        _connection.Rows = Table(["Title", "Name"], ["x", "a"]);

        var row = Assert.Single(AsList(Invoke("Columns")));

        Assert.Equal($"SELECT [t0].[Title], [t1].[Name] {Join}", _connection.LastCommand.CommandText);
        Assert.Equal(("x", "a"), (Get(row, "Title"), Get(row, "Name")));
    }

    [Fact]
    public void WhereOrderAndSelect_ReadThroughThePair()
    {
        _connection.Rows = Table(["Title", "Name"], ["x", "a"]);

        Assert.Single(AsList(Invoke("ThroughThePair", 2)));

        Assert.Equal(
            $"SELECT [t0].[Title], [t1].[Name] {Join} WHERE [t1].[Rating] > @p0 ORDER BY [t1].[Name], [t0].[Title] DESC",
            _connection.LastCommand.CommandText);
        Assert.Equal([2], Parameters());
    }

    [Fact]
    public void Entities_ReadBothWithTheInnerFilter()
    {
        _connection.Rows = Table(["Id", "BlogId", "Title", "Id", "Name", "Rating"], [10, 1, "x", 1, "a", 5]);

        var row = Assert.Single(AsList(Invoke("Entities", "hidden")));

        Assert.Equal(
            $"SELECT [t0].[Id], [t0].[BlogId], [t0].[Title], [t1].[Id], [t1].[Name], [t1].[Rating] {Join} WHERE [t1].[Name] <> @p0",
            _connection.LastCommand.CommandText);
        Assert.Equal("a", Get(Get(row, "b")!, "Name"));
        Assert.Equal(10, Get(Get(row, "p")!, "Id"));
    }

    [Fact]
    public void TwoJoins_JoinTheSecondThroughTheFirst()
    {
        _connection.Rows = Table(["Title", "Title"], ["x", "y"]);

        Assert.Single(AsList(Invoke("TwoJoins")));

        Assert.Equal(
            $"SELECT [t0].[Title], [t2].[Title] {Join} INNER JOIN [Posts] AS [t2] ON [t1].[Id] = [t2].[BlogId]",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task CountAndFirst_KeepTheJoin()
    {
        _connection.Scalar = 3;
        Assert.Equal(3, Invoke("CountJoined"));
        Assert.Equal($"SELECT COUNT(*) {Join}", _connection.LastCommand.CommandText);

        _connection.Rows = Table(["Name"], ["a"]);
        Assert.Equal("a!", await InvokeAsync("FirstNameAsync", "!", Token));
        Assert.Equal($"SELECT TOP(1) [t1].[Name] {Join} ORDER BY [t0].[Id]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task PredicateTerminals_FilterThroughTheProjection()
    {
        _connection.Scalar = 2;
        Assert.Equal(2, Invoke("CountThroughThePair", 3));
        Assert.Equal($"SELECT COUNT(*) {Join} WHERE [t1].[Rating] > @p0", _connection.LastCommand.CommandText);
        Assert.Equal([3], Parameters());

        _connection.Rows = Table(["Name"], ["b"]);
        Assert.Equal("b", await InvokeAsync("FirstColumnAsync", "a", Token));
        Assert.Equal("SELECT TOP(1) [t0].[Name] FROM [Blogs] AS [t0] WHERE [t0].[Name] <> @p0 ORDER BY [t0].[Name]", _connection.LastCommand.CommandText);
        Assert.Equal(["a"], Parameters());
    }

    private object?[] Parameters() => [.. _connection.LastCommand.Parameters.Cast<DbParameter>().Select(static p => p.Value)];

    private static DataTable Table(string[] columns, params object[][] rows)
    {
        var table = new DataTable();
        for (var i = 0; i < columns.Length; i++)
        {
            // A statement can select a column name twice; the reader reads by ordinal.
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
