using System.Collections;
using System.Data;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class ProjectionInterceptorTests : IDisposable
{
    // Compiled, generated and loaded once: every test runs the same interceptors and readers.
    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("ProjectionInterceptors", ProjectionSources.Source));
        return (run, run.Load());
    });

    private readonly FakeDbConnection _connection = new();
    private readonly DbContext _db;
    private readonly object _queries;

    public ProjectionInterceptorTests()
    {
        var assembly = Compiled.Value.Assembly;
        _db = (DbContext)Activator.CreateInstance(assembly.GetType("Shop.ShopDb")!, _connection)!;
        _queries = Activator.CreateInstance(assembly.GetType("Shop.Queries")!, _db)!;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Interceptors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(InterceptorEmitter.HintName), "ProjectionInterceptors");

    [Fact]
    public void Interceptors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void AnonymousType_ReadsOnlyItsColumns()
    {
        _connection.Rows = Table(["Id", "Name"], [1, "alpha"], [2, DBNull.Value]);

        var rows = AsList(Invoke("Anonymous"));

        Assert.Equal("SELECT [t0].[Id], [t0].[Name] FROM [Blogs] AS [t0]", _connection.LastCommand.CommandText);
        Assert.Equal([1, 2], rows.Select(r => Get(r, "Id")));
        Assert.Equal(["alpha", null], rows.Select(r => Get(r, "Name")));
    }

    [Fact]
    public void NestedAnonymousType_ReadsEveryColumn()
    {
        _connection.Rows = Table(["Id", "Name", "Status", "Views"], [1, "alpha", 1, DBNull.Value]);

        var inner = Get(Assert.Single(AsList(Invoke("Nested"))), "Inner")!;

        Assert.Equal("Published", Get(inner, "Status")!.ToString());
        Assert.Null(Get(inner, "Views"));
    }

    [Theory]
    [InlineData("Records")]
    [InlineData("Initializers")]
    public void NamedTypes_AreConstructedAsInTheLambda(string method)
    {
        _connection.Rows = Table(["Id", "Name"], [3, "gamma"]);

        var row = Assert.Single(AsList(Invoke(method)));

        Assert.Equal(3, Get(row, "Id"));
        Assert.Equal("gamma", Get(row, "Name"));
    }

    [Fact]
    public void SingleMembersTuplesAndConversions_KeepTheirTypes()
    {
        _connection.Rows = Table(["Name"], ["alpha"]);
        Assert.Equal(["alpha"], (List<string>)Invoke("Names")!);

        _connection.Rows = Table(["Id", "Views"], [1, DBNull.Value]);
        Assert.Equal([(1, (int?)null)], (List<(int, int?)>)Invoke("Tuples")!);

        _connection.Rows = Table(["Id"], [7]);
        Assert.Equal([(object)7], (List<object>)Invoke("Boxed")!);
        Assert.Equal([7L], (List<long>)Invoke("Widened")!);
    }

    [Fact]
    public void ColumnThroughOptionalNavigation_IsLeftJoined_AndNullWithoutIt()
    {
        _connection.Rows = Table(["Name"], ["Ann"], [DBNull.Value]);

        Assert.Equal(["Ann", null], (List<string?>)Invoke("OwnerNames")!);
        Assert.Equal(
            "SELECT [t1].[Name] FROM [Blogs] AS [t0] LEFT JOIN [Authors] AS [t1] ON [t0].[OwnerId] = [t1].[Id]",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void ValueThroughOptionalNavigation_IsNullWhenCastToNullable_AndThrowsOtherwise_AsInEfCore()
    {
        _connection.Rows = Table(["Id"], [1], [DBNull.Value]);
        Assert.Equal([1, null], (List<int?>)Invoke("NullableOwnerIds")!);

        _connection.Rows = Table(["Id"], [DBNull.Value]);
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke("OwnerIds"));
        Assert.Equal("Nullable object must have a value.", Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
    }

    [Fact]
    public void EntityThroughNavigation_IsReadByTheRuntime()
    {
        _connection.Rows = Table(["Id", "Name"], [1, "Ann"], [DBNull.Value, DBNull.Value]);

        var owners = ((IEnumerable)Invoke("Owners")!).Cast<object?>().ToList();

        Assert.Equal([1, null], owners.Select(o => o is null ? null : Get(o, "Id")));
        Assert.Equal("Ann", Get(owners[0]!, "Name"));
    }

    [Fact]
    public void EntitiesAndColumns_ReachTheReaderTogether()
    {
        _connection.Rows = Table(
            ["BlogName", "PostId", "PostBlogId", "Title", "Id", "Name", "OwnerId", "Status", "Views"],
            ["alpha", 10, 1, "first", 1, "alpha", DBNull.Value, 0, DBNull.Value]);

        var row = Assert.Single(AsList(Invoke("PostsWithBlogs")));

        Assert.Equal("alpha", Get(row, "BlogName"));
        Assert.Equal(10, Get(Get(row, "p")!, "Id"));
        Assert.Equal(1, Get(Get(row, "Blog")!, "Id"));
        Assert.Contains("INNER JOIN [Blogs] AS [t1] ON [t0].[BlogId] = [t1].[Id]", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludeOnTheProjectedEntity_LoadsItsCollection()
    {
        _connection.RowSets.Enqueue(Table(["Name", "Id", "BlogName", "OwnerId", "Status", "Views"], ["alpha", 1, "alpha", DBNull.Value, 0, DBNull.Value]));
        _connection.RowSets.Enqueue(Table(["Id", "BlogId", "Title"], [10, 1, "first"], [11, 1, "second"]));

        var blog = Get(Assert.Single(AsList(Invoke("BlogsWithPosts"))), "b")!;

        Assert.Equal([10, 11], AsList(Get(blog, "Posts")).Select(p => Get(p, "Id")));
        Assert.Equal(2, _connection.Commands.Count);
    }

    [Fact]
    public void PagingAfterSelect_PagesTheRows()
    {
        _connection.Rows = Table(["Name"], ["beta"]);

        Assert.Equal(["beta"], (List<string>)Invoke("Page", 1, 1)!);
        Assert.Equal(
            "SELECT [t0].[Name] FROM [Blogs] AS [t0] ORDER BY [t0].[Name] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY",
            _connection.LastCommand.CommandText);
    }

    [Fact]
    public void QueryFromALocalVariable_IsIntercepted()
    {
        _connection.Rows = Table(["Name"], ["alpha"]);

        Assert.Equal(["alpha"], (List<string>)Invoke("FromVariable")!);
        Assert.Equal("SELECT TOP(@p0) [t0].[Name] FROM [Blogs] AS [t0]", _connection.LastCommand.CommandText);
    }

    [Fact]
    public async Task Terminals_RunTheProjection()
    {
        _connection.Rows = Table(["Name"], ["alpha"]);
        Assert.Equal(["alpha"], (string[])Invoke("Array")!);
        Assert.Equal(["alpha"], (List<string>)(await InvokeAsync("ListAsync", Token))!);
        Assert.Equal(["alpha"], (string[])(await InvokeAsync("ArrayAsync", Token))!);
        Assert.Equal("alpha", Invoke("First"));
        Assert.StartsWith("SELECT TOP(1) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.Equal("alpha", await InvokeAsync("FirstOrDefaultAsync", Token));

        _connection.Rows = Table(["Id", "Name"], [1, "alpha"]);
        Assert.Equal(1, Get(Invoke("Single")!, "Id"));
        Assert.StartsWith("SELECT TOP(2) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.Equal(1, Get((await InvokeAsync("SingleOrDefaultAsync", Token))!, "Id"));
    }

    [Fact]
    public async Task ScalarTerminals_IgnoreTheProjection()
    {
        _connection.Scalar = 2;
        Assert.Equal(2, Invoke("Count"));
        Assert.Equal("SELECT COUNT(*) FROM [Blogs] AS [t0]", _connection.LastCommand.CommandText);
        Assert.Equal(2L, await InvokeAsync("LongCountAsync", Token));

        _connection.Scalar = true;
        Assert.Equal(true, Invoke("Any"));
        Assert.Equal(true, await InvokeAsync("AnyAsync", Token));
    }

    [Fact]
    public void InMemoryQuery_FallsBackToLinqToObjects()
    {
        var blogType = Compiled.Value.Assembly.GetType("Shop.Blog")!;
        var blogs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(blogType))!;
        var blog = Activator.CreateInstance(blogType)!;
        blogType.GetProperty("Name")!.SetValue(blog, "alpha");
        blogs.Add(blog);

        var names = Compiled.Value.Assembly.GetType("Shop.Queries")!.GetMethod("InMemory")!.Invoke(null, [blogs]);

        Assert.Equal(["alpha"], (List<string>)names!);
        Assert.Empty(_connection.Commands);
    }

    private static DataTable Table(string[] columns, params object[][] rows)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(object)).AllowDBNull = true;
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
