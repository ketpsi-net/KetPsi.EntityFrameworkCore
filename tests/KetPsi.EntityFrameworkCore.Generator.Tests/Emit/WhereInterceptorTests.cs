using System.Reflection;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class WhereInterceptorTests
{
    private const string SelectAll =
        "SELECT [t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Rating], [t0].[Status], [t0].[Views] FROM [Blogs] AS [t0]";

    // Compiled, generated and loaded once: every test runs the same interceptors.
    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("WhereInterceptors", WhereInterceptorSources.Source));
        return (run, run.Load());
    });

    [Fact]
    public void Interceptors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(InterceptorEmitter.HintName), "WhereInterceptors");

    [Fact]
    public void Interceptors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void CapturedParameter_BecomesParameterValue()
        => AssertSql("ByParameter", [5], " WHERE [t0].[Rating] > @p0", 5);

    [Fact]
    public void CapturedLocalAndInstanceField_AreRead()
        => AssertSql("ByLocalAndField", [], " WHERE ([t0].[Name] = @p0 AND [t0].[Rating] >= @p1)", "x", 4);

    [Fact]
    public void StaticField_IsRead()
        => AssertSql("ByStatic", [], " WHERE [t0].[Rating] < @p0", 50);

    [Fact]
    public void PropertyChain_IsRead()
        => AssertSql("ByChain", [Filter("abc")], " WHERE [t0].[Name] = @p0", "abc");

    [Fact]
    public void EnumConstant_IsParameter()
        => AssertSql("ByEnum", [], " WHERE [t0].[Status] = @p0", 1);

    [Fact]
    public void Constant_IsParameter()
        => AssertSql("ByConstant", [], " WHERE [t0].[Rating] >= @p0", 3);

    [Fact]
    public void NullableValue_DecidesNullHandlingAtRuntime()
    {
        AssertSql("ByNullable", [7], " WHERE [t0].[Views] = @p0", 7);
        AssertSql("ByNullable", [null], " WHERE [t0].[Views] IS NULL");
    }

    [Fact]
    public void ListContains_PassesJsonList()
        => AssertSql(
            "ByList",
            [new List<int> { 1, 2 }],
            " WHERE [t0].[Id] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$'))",
            "[1,2]");

    [Fact]
    public void ArrayContains_PassesJsonList()
        => AssertSql(
            "ByArray",
            [new[] { 3 }],
            " WHERE [t0].[Id] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$'))",
            "[3]");

    [Fact]
    public void NegatedOr_UsesDeMorgan()
        => AssertSql("ByNot", [], " WHERE ([t0].[IsActive] = CAST(0 AS bit) AND [t0].[Views] IS NULL)");

    [Fact]
    public void WhereIf_True_AppliesPredicate()
        => AssertSql("Optional", [Filter("abc")], " WHERE [t0].[Name] = @p0", "abc");

    [Fact]
    public void WhereIf_False_SkipsPredicateWithoutReadingValues()
        => AssertSql("Optional", [null], "");

    [Fact]
    public void QueryComposedAcrossStatements_KeepsEveryPredicate()
    {
        AssertSql("Composed", [2], " WHERE [t0].[Rating] > @p0 AND [t0].[IsActive] = CAST(1 AS bit)", 2);
        AssertSql("Composed", [null], " WHERE [t0].[IsActive] = CAST(1 AS bit)");
    }

    [Fact]
    public void InMemoryQuery_FallsBackToLinqToObjects()
    {
        var assembly = Compiled.Value.Assembly;
        var blogType = assembly.GetType("E2E.Blog")!;
        var blogs = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(blogType))!;
        foreach (var rating in new[] { 1, 5 })
        {
            var blog = Activator.CreateInstance(blogType)!;
            blogType.GetProperty("Rating")!.SetValue(blog, rating);
            blogs.Add(blog);
        }

        var result = (System.Collections.IList)assembly.GetType("E2E.Repository")!.GetMethod("InMemory")!.Invoke(null, [blogs, 3])!;

        Assert.Equal(5, blogType.GetProperty("Rating")!.GetValue(Assert.Single(result.Cast<object>())));
    }

    private static object Filter(string name)
        => Activator.CreateInstance(Compiled.Value.Assembly.GetType("E2E.Filter")!, name)!;

    private static void AssertSql(string method, object?[] arguments, string where, params object[] values)
    {
        var assembly = Compiled.Value.Assembly;
        using var db = (DbContext)Activator.CreateInstance(assembly.GetType("E2E.E2EDb")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("E2E.Repository")!, db)!;

        var query = repository.GetType().GetMethod(method)!.Invoke(repository, arguments)!;

        var tail = (QueryNode)query.GetType().GetProperty("Tail")!.GetValue(query)!;
        var entity = ModelBinder.GetEntity(db.Model, assembly.GetType("E2E.Blog")!);
        var statement = SqlBuilder.Build(entity, tail, SqlServerDialect.Instance, QueryTerminal.Rows);

        Assert.Equal(SelectAll + where, statement.CommandText);
        Assert.Equal(values, statement.Parameters.Select(p => p.Value is Enum e ? Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture) : p.Value));
    }
}
