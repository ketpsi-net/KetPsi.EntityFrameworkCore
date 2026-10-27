using System.Data;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class QueryFilterGenerationTests : IDisposable
{
    private const int Tenant = 7;

    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("QueryFilters", QueryFilterSources.Source));
        return (run, run.Load());
    });

    private readonly FakeDbConnection _connection = new() { Rows = new DataTable() };
    private readonly DbContext _db;
    private readonly object _queries;

    public QueryFilterGenerationTests()
    {
        var assembly = Compiled.Value.Assembly;
        _db = (DbContext)Activator.CreateInstance(assembly.GetType("Filtered.FilteredDb")!, _connection, Tenant)!;
        _queries = Activator.CreateInstance(assembly.GetType("Filtered.Queries")!, _db)!;
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Filters_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(QueryFilterEmitter.HintName), "QueryFilters");

    [Fact]
    public void Filters_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void Query_AppliesEveryFilterWithValuesFromTheContext()
    {
        Invoke("Blogs");

        var sql = _connection.LastCommand.CommandText;
        Assert.Contains("[t0].[IsDeleted] = CAST(0 AS bit)", sql, StringComparison.Ordinal);
        Assert.Contains("[t0].[TenantId] = @p", sql, StringComparison.Ordinal);
        Assert.Contains("[t0].[Rating] >= @p", sql, StringComparison.Ordinal);
        Assert.Contains("[t0].[Status] <> @p", sql, StringComparison.Ordinal);
        Assert.Contains("[t0].[Views] > @p", sql, StringComparison.Ordinal);
        Assert.Equivalent(new object[] { Tenant, 2, 0, 100 }, Parameters());
    }

    [Fact]
    public void ContextValues_AreReadWhenTheQueryRuns()
    {
        _db.GetType().GetProperty("MinimumRating")!.SetValue(_db, 4);

        Invoke("Blogs");

        Assert.Contains(4, Parameters());
    }

    [Fact]
    public void IgnoreNamedFilter_DropsOnlyThatFilter()
    {
        Invoke("WithoutTenant");

        var sql = _connection.LastCommand.CommandText;
        Assert.DoesNotContain("[t0].[TenantId] = @p", sql, StringComparison.Ordinal);
        Assert.Contains("[t0].[IsDeleted] = CAST(0 AS bit)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreAllFilters_DropsEveryFilter()
    {
        Invoke("Unfiltered");

        Assert.DoesNotContain("WHERE", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationClassFilter_AppliesToTheContext()
    {
        _connection.Scalar = 0;

        Invoke("Tags");

        Assert.Equal("SELECT COUNT(*) FROM [Tags] AS [t0] WHERE [t0].[Name] <> @p0", _connection.LastCommand.CommandText);
        Assert.Equal(["hidden"], Parameters());
    }

    private object? Invoke(string method) => _queries.GetType().GetMethod(method)!.Invoke(_queries, null);

    private object?[] Parameters() => [.. _connection.LastCommand.Parameters.Cast<SqlParameter>().Select(p => p.Value)];
}
