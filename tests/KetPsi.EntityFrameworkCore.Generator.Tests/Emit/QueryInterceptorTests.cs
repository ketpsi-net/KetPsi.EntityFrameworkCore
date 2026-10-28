using System.Collections;
using System.Data;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class QueryInterceptorTests : IDisposable
{
    private const string Columns = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Rating], [t0].[Status], [t0].[Views]";
    private const string Table = "[Blogs] AS [t0]";
    private const string SelectAll = "SELECT " + Columns + " FROM " + Table;

    // Compiled, generated and loaded once: every test runs the same interceptors and readers.
    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("QueryInterceptors", QuerySources.Source));
        return (run, run.Load());
    });

    private readonly FakeDbConnection _connection = new();
    private readonly DbContext _db;
    private readonly object _queries;

    public QueryInterceptorTests()
    {
        var assembly = Compiled.Value.Assembly;
        _db = (DbContext)Activator.CreateInstance(assembly.GetType("Shop.ShopDb")!, _connection)!;
        _queries = Activator.CreateInstance(assembly.GetType("Shop.Queries")!, _db)!;
        _connection.Rows = Rows((1, "alpha", 5, true, 10), (2, "beta", 3, false, null));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Type BlogType => Compiled.Value.Assembly.GetType("Shop.Blog")!;

    private static Type PostType => Compiled.Value.Assembly.GetType("Shop.Post")!;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Interceptors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(InterceptorEmitter.HintName), "QueryInterceptors");

    [Fact]
    public void Interceptors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void FullChain_BuildsOneStatementAndMaterializes()
    {
        var blogs = AsList(Invoke("Page", 3, 1, 10));

        Assert.Equal(
            SelectAll + " WHERE [t0].[Rating] >= @p0 ORDER BY [t0].[Name], [t0].[Id] DESC OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY",
            _connection.LastCommand.CommandText);
        Assert.Equal([3, 1, 10], Parameters());
        Assert.Equal(["alpha", "beta"], blogs.Select(b => Get(b, "Name")));
        Assert.Equal([10, null], blogs.Select(b => Get(b, "Views")));
        Assert.Equal("Draft", Get(blogs[0], "Status")!.ToString());
    }

    [Fact]
    public void ToArray_WithOrdering_ReadsRows()
    {
        var blogs = (Array)Invoke("ByRating")!;

        Assert.Equal(SelectAll + " ORDER BY [t0].[Rating] DESC, [t0].[Id]", _connection.LastCommand.CommandText);
        Assert.Equal(2, blogs.Length);
    }

    [Fact]
    public async Task AsyncListTerminals_ReadRows()
    {
        Assert.Equal(2, AsList(await InvokeAsync("ListAsync", Token)).Count);
        Assert.Equal(2, ((Array)(await InvokeAsync("ArrayAsync", Token))!).Length);
        Assert.Equal(SelectAll, _connection.LastCommand.CommandText);
    }

    [Fact]
    public void First_WithPredicate_UsesTopOne()
    {
        var blog = Invoke("First", 2);

        Assert.Equal("SELECT TOP(1) " + Columns + " FROM " + Table + " WHERE [t0].[Id] = @p0", _connection.LastCommand.CommandText);
        Assert.Equal([2], Parameters());
        Assert.Equal("alpha", Get(blog!, "Name"));
    }

    [Fact]
    public async Task FirstAsync_ReadsFirstRow()
        => Assert.Equal(1, Get((await InvokeAsync("FirstAsync", Token))!, "Id"));

    [Fact]
    public void FirstOrDefault_NoRows_ReturnsNull()
    {
        _connection.Rows = Rows();

        Assert.Null(Invoke("FirstOrDefault"));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithPredicate_SendsParameter()
    {
        var blog = await InvokeAsync("FirstOrDefaultAsync", "beta", Token);

        Assert.Equal("SELECT TOP(1) " + Columns + " FROM " + Table + " WHERE [t0].[Name] = @p0", _connection.LastCommand.CommandText);
        Assert.Equal(["beta"], Parameters());
        Assert.NotNull(blog);
    }

    [Fact]
    public void Single_TwoRows_ThrowsLikeLinq()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke("Single"));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.StartsWith("SELECT TOP(2) ", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleAsync_WithPredicate_ReadsRow()
    {
        _connection.Rows = Rows((7, "only", 1, true, null));

        Assert.Equal(7, Get((await InvokeAsync("SingleAsync", 7, Token))!, "Id"));
        Assert.Equal([7], Parameters());
    }

    [Fact]
    public async Task SingleOrDefault_Variants_FollowLinqRules()
    {
        _connection.Rows = Rows();

        Assert.Null(Invoke("SingleOrDefault", 1));
        Assert.Null(await InvokeAsync("SingleOrDefaultAsync", Token));
    }

    [Fact]
    public async Task CountTerminals_ReturnScalar()
    {
        _connection.Scalar = 4;
        Assert.Equal(4, Invoke("Count"));
        Assert.Equal("SELECT COUNT(*) FROM " + Table, _connection.LastCommand.CommandText);

        Assert.Equal(4, await InvokeAsync("CountAsync", true, Token));
        Assert.Equal("SELECT COUNT(*) FROM " + Table + " WHERE [t0].[IsActive] = @p0", _connection.LastCommand.CommandText);
        Assert.Equal([true], Parameters());
    }

    [Fact]
    public async Task LongCountTerminals_ReturnScalar()
    {
        _connection.Scalar = 9L;

        Assert.Equal(9L, Invoke("LongCount"));
        Assert.Equal("SELECT COUNT_BIG(*) FROM " + Table + " WHERE [t0].[IsActive] = CAST(1 AS bit)", _connection.LastCommand.CommandText);
        Assert.Equal(9L, await InvokeAsync("LongCountAsync", Token));
    }

    [Fact]
    public async Task AnyTerminals_ReturnScalar()
    {
        _connection.Scalar = true;

        Assert.Equal(true, Invoke("Any", 4));
        Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM " + Table + " WHERE [t0].[Rating] > @p0) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            _connection.LastCommand.CommandText);
        Assert.Equal(true, await InvokeAsync("AnyAsync", Token));
    }

    [Fact]
    public void IgnoreQueryFilters_BothOverloads_Run()
    {
        Assert.Equal(2, AsList(Invoke("IgnoringFilters")).Count);

        _connection.Scalar = 1;
        Assert.Equal(1, Invoke("IgnoringNamedFilter"));
    }

    [Theory]
    [InlineData(true, "ORDER BY [t0].[Name]")]
    [InlineData(false, "ORDER BY [t0].[Rating] DESC")]
    public void QueryComposedAcrossStatements_RunsTheChosenOrdering(bool byName, string orderBy)
    {
        Invoke("Composed", byName);

        Assert.Equal("SELECT TOP(@p0) " + Columns + " FROM " + Table + " " + orderBy, _connection.LastCommand.CommandText);
    }

    [Fact]
    public void TrackingQueryAcrossStatements_AttachesEntities()
    {
        var blogs = AsList(Invoke("TrackedAcrossStatements"));

        Assert.Equal(SelectAll, _connection.LastCommand.CommandText);
        Assert.All(blogs, blog => Assert.Equal(EntityState.Unchanged, _db.Entry(blog).State));
        Assert.Equal(2, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void AsTracking_AttachesEntities()
    {
        var blogs = AsList(Invoke("Tracked", 3));

        Assert.Equal(SelectAll + " WHERE [t0].[Rating] >= @p0", _connection.LastCommand.CommandText);
        Assert.All(blogs, blog => Assert.Equal(EntityState.Unchanged, _db.Entry(blog).State));
    }

    [Theory]
    [InlineData(QueryTrackingBehavior.TrackAll, 2)]
    [InlineData(QueryTrackingBehavior.NoTracking, 0)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, 0)]
    public void AsTrackingWithBehavior_FollowsBehavior(QueryTrackingBehavior behavior, int tracked)
    {
        Assert.Equal(2, AsList(Invoke("TrackedWithBehavior", behavior)).Count);

        Assert.Equal(tracked, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void AsNoTrackingWithIdentityResolution_ResolvesWithoutTracking()
    {
        _connection.Rows = Rows((1, "alpha", 5, true, 10), (1, "again", 3, false, null));

        var blogs = AsList(Invoke("IdentityResolved"));

        Assert.Same(blogs[0], blogs[1]);
        Assert.Equal("alpha", Get(blogs[1], "Name"));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void Include_Collection_LoadsChildrenWithGeneratedAccessors()
    {
        _connection.RowSets.Enqueue(Rows((1, "alpha", 5, true, 10), (2, "beta", 3, false, null)));
        _connection.RowSets.Enqueue(PostRows((10, 1, "first"), (11, 1, "second")));

        var blogs = AsList(Invoke("WithPosts"));

        Assert.Equal(SelectAll + " ORDER BY [t0].[Id]", _connection.Commands[0].CommandText);
        Assert.Contains("FROM [Posts] AS [t0] WHERE [t0].[BlogId] IN (SELECT [value] FROM OPENJSON(@p0)", _connection.Commands[1].CommandText, StringComparison.Ordinal);
        var posts = AsList(Get(blogs[0], "Posts"));
        Assert.Equal([10, 11], posts.Select(p => PostType.GetProperty("Id")!.GetValue(p)));
        Assert.All(posts, p => Assert.Same(blogs[0], PostType.GetProperty("Blog")!.GetValue(p)));
        Assert.Empty(AsList(Get(blogs[1], "Posts")));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void IncludeThenInclude_JoinsReferenceAndLoadsItsCollection()
    {
        var main = new DataTable();
        foreach (var column in new[] { "PostId", "PostBlogId", "PostTitle", "Id", "IsActive", "Name", "Rating", "Status", "Views" })
        {
            main.Columns.Add(column, typeof(object));
        }

        main.Rows.Add(10, 1, "first", 1, true, "alpha", 5, 0, DBNull.Value);
        _connection.RowSets.Enqueue(main);
        _connection.RowSets.Enqueue(PostRows((10, 1, "first"), (11, 1, "second")));

        var posts = AsList(Invoke("PostsWithBlogAndSiblings"));

        Assert.StartsWith(
            "SELECT [t0].[Id], [t0].[BlogId], [t0].[Title], [t1].[Id]",
            _connection.Commands[0].CommandText,
            StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [Blogs] AS [t1] ON [t0].[BlogId] = [t1].[Id]", _connection.Commands[0].CommandText, StringComparison.Ordinal);
        var blog = PostType.GetProperty("Blog")!.GetValue(Assert.Single(posts))!;
        Assert.Equal([10, 11], AsList(Get(blog, "Posts")).Select(p => PostType.GetProperty("Id")!.GetValue(p)).Order());
        Assert.True(_db.Entry(blog).Navigation("Posts").IsLoaded);
        Assert.Equal(3, _db.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void InMemoryQuery_FallsBackToLinqToObjects()
    {
        var blogs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(BlogType))!;
        foreach (var rating in new[] { 4, 2 })
        {
            var blog = Activator.CreateInstance(BlogType)!;
            BlogType.GetProperty("Rating")!.SetValue(blog, rating);
            blogs.Add(blog);
        }

        var result = AsList(Compiled.Value.Assembly.GetType("Shop.Queries")!.GetMethod("InMemory")!.Invoke(null, [blogs]));

        Assert.Equal(2, Get(Assert.Single(result), "Rating"));
        Assert.Empty(_connection.Commands);
    }

    private static DataTable Rows(params (int Id, string Name, int Rating, bool IsActive, int? Views)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Rating", typeof(int));
        table.Columns.Add("Status", typeof(int));
        table.Columns.Add("Views", typeof(int)).AllowDBNull = true;
        foreach (var row in rows)
        {
            table.Rows.Add(row.Id, row.IsActive, row.Name, row.Rating, 0, row.Views is { } views ? views : DBNull.Value);
        }

        return table;
    }

    private static DataTable PostRows(params (int Id, int BlogId, string Title)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("BlogId", typeof(int));
        table.Columns.Add("Title", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.Id, row.BlogId, row.Title);
        }

        return table;
    }

    private static List<object> AsList(object? value) => [.. ((IEnumerable)value!).Cast<object>()];

    private static object? Get(object blog, string property) => BlogType.GetProperty(property)!.GetValue(blog);

    private object? Invoke(string method, params object?[] arguments)
        => _queries.GetType().GetMethod(method)!.Invoke(_queries, arguments);

    private async Task<object?> InvokeAsync(string method, params object?[] arguments)
    {
        var task = (Task)Invoke(method, arguments)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private object?[] Parameters()
        => [.. _connection.LastCommand.Parameters.Cast<SqlParameter>().Select(p => p.Value)];
}
