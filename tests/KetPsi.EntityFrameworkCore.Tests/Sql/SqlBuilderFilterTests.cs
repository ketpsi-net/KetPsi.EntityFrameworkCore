using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderFilterTests
{
    private const string Columns0 = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score]";
    private const string Columns1 = "[t1].[Id], [t1].[IsActive], [t1].[Name], [t1].[Score]";
    private const string SelectAll = "SELECT " + Columns0 + " FROM [shop].[Items] AS [t0]";

    private static readonly ComparisonTemplate IdEquals = new(nameof(Item.Id), ComparisonOperator.Equal, 0);

    public SqlBuilderFilterTests()
    {
        TestQueryFilters.EnsureRegistered();
    }

    [Fact]
    public void Filter_IsAppliedBeforeUserPredicates()
    {
        using var db = new FilteredItemContext();

        var statement = Build(db, q => SqlQuery.Where(q, IdEquals, [3]));

        Assert.Equal(SelectAll + " WHERE [t0].[IsActive] = CAST(1 AS bit) AND [t0].[Id] = @p0", statement.CommandText);
    }

    [Fact]
    public void Filter_WithoutOperators_StillFilters()
    {
        using var db = new FilteredItemContext();

        Assert.Equal(SelectAll + " WHERE [t0].[IsActive] = CAST(1 AS bit)", Build(db, q => q).CommandText);
    }

    [Fact]
    public void FilterValues_ComeFromContextAndAreNumberedFirst()
    {
        using var db = new TenantItemContext { TenantScore = 7 };
        var entity = ModelBinder.GetEntity(db.Model, typeof(Item));

        var statement = Build(db, q => SqlQuery.Where(q, IdEquals, [3]));

        Assert.Equal(SelectAll + " WHERE [t0].[Score] = @p0 AND [t0].[Id] = @p1", statement.CommandText);
        Assert.Equal(
            [
                new QueryParameter("@p0", 7, entity.GetProperty(nameof(Item.Score))),
                new QueryParameter("@p1", 3, entity.GetProperty(nameof(Item.Id))),
            ],
            statement.Parameters);
    }

    [Fact]
    public void FilterValue_Null_UsesIsNull()
    {
        using var db = new TenantItemContext { TenantScore = null };

        var statement = Build(db, q => q);

        Assert.Equal(SelectAll + " WHERE [t0].[Score] IS NULL", statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public void Filter_StaysOnRootTableWhenQueryIsWrapped()
    {
        using var db = new FilteredItemContext();

        var statement = Build(db, q => SqlQuery.Where(SqlQuery.Take(q, 10), IdEquals, [3]));

        Assert.Equal(
            "SELECT " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM [shop].[Items] AS [t0]" +
            " WHERE [t0].[IsActive] = CAST(1 AS bit)) AS [t1] WHERE [t1].[Id] = @p1",
            statement.CommandText);
    }

    [Fact]
    public void IgnoreQueryFilters_RemovesFilter()
    {
        using var db = new FilteredItemContext();

        Assert.Equal(SelectAll, Build(db, q => SqlQuery.IgnoreQueryFilters(q)).CommandText);
    }

    [Fact]
    public void IgnoreQueryFilters_AfterOtherOperators_StillRemovesFilter()
    {
        using var db = new FilteredItemContext();

        var statement = Build(db, q => SqlQuery.IgnoreQueryFilters(SqlQuery.Where(q, IdEquals, [3])));

        Assert.Equal(SelectAll + " WHERE [t0].[Id] = @p0", statement.CommandText);
    }

    [Fact]
    public void Filter_AppliesToCount()
    {
        using var db = new FilteredItemContext();

        Assert.Equal(
            "SELECT COUNT(*) FROM [shop].[Items] AS [t0] WHERE [t0].[IsActive] = CAST(1 AS bit)",
            Build(db, q => q, QueryTerminal.Count).CommandText);
    }

    [Fact]
    public void Filter_AppliesToAny()
    {
        using var db = new FilteredItemContext();

        Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM [shop].[Items] AS [t0] WHERE [t0].[IsActive] = CAST(1 AS bit))" +
            " THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Build(db, q => q, QueryTerminal.Any).CommandText);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void NamedFilters_AreAllApplied()
    {
        using var db = new NamedFilterItemContext();

        var commandText = Build(db, q => q).CommandText;

        Assert.StartsWith(SelectAll + " WHERE ", commandText, StringComparison.Ordinal);
        Assert.Contains("[t0].[IsActive] = CAST(1 AS bit)", commandText, StringComparison.Ordinal);
        Assert.Contains("[t0].[Score] IS NOT NULL", commandText, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreNamedFilter_RemovesOnlyThatFilter()
    {
        using var db = new NamedFilterItemContext();

        var statement = Build(db, q => SqlQuery.IgnoreQueryFilters(q, [NamedFilterItemContext.ActiveFilter]));

        Assert.Equal(SelectAll + " WHERE [t0].[Score] IS NOT NULL", statement.CommandText);
    }
#endif

    private static SqlStatement Build(DbContext db, Func<IQueryable<Item>, IQueryable<Item>> compose, QueryTerminal terminal = QueryTerminal.Rows)
    {
        var query = (SqlQueryable<Item>)compose(SqlQuery.ToSqlQueryable(db.Set<Item>()));
        var entity = ModelBinder.GetEntity(db.Model, typeof(Item));
        var filters = QueryFilterResolver.Resolve(db, entity, IgnoredQueryFilters.FromChain(query.Tail));

        return SqlBuilder.Build(entity, query.Tail, SqlServerDialect.Instance, terminal, filters: filters);
    }
}
