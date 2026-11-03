using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderProjectionTests : SqlBuilderTestBase
{
    private static readonly ComparisonTemplate IdAbove = new(nameof(Item.Id), ComparisonOperator.GreaterThan, 0);

    [Fact]
    public void Select_ReadsOnlyProjectedColumns()
        => Assert.Equal(
            "SELECT [t0].[Name], [t0].[Id] FROM [shop].[Items] AS [t0]",
            Projected(Select(Items, nameof(Item.Name), nameof(Item.Id))));

    [Fact]
    public void Select_RepeatedProperty_IsSelectedOnce()
        => Assert.Equal(
            "SELECT [t0].[Name] FROM [shop].[Items] AS [t0]",
            Projected(Select(Items, nameof(Item.Name), nameof(Item.Name))));

    [Fact]
    public void Select_KeepsFiltersAndOrderingBeforeIt()
        => Assert.Equal(
            "SELECT [t0].[Name] FROM [shop].[Items] AS [t0] WHERE [t0].[Id] > @p0 ORDER BY [t0].[Score]",
            Projected(Select(SqlQuery.OrderBy(Where(Items, IdAbove, 3), nameof(Item.Score)), nameof(Item.Name))));

    [Fact]
    public void Select_AfterPaging_ProjectsThePage()
        => Assert.Equal(
            "SELECT [t0].[Name] FROM [shop].[Items] AS [t0] ORDER BY [t0].[Id] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY",
            Projected(Select(SqlQuery.Take(SqlQuery.Skip(SqlQuery.OrderBy(Items, nameof(Item.Id)), 2), 3), nameof(Item.Name))));

    [Fact]
    public void TakeAfterSelect_LimitsTheProjectedRows()
        => Assert.Equal(
            "SELECT TOP(@p0) [t0].[Name] FROM [shop].[Items] AS [t0]",
            Projected(SqlProjection.Take(Select(Items, nameof(Item.Name)), 5)));

    [Fact]
    public void DerivedTableOfAProjection_KeepsTheOrderingColumns()
    {
        var query = SqlProjection.Skip(SqlProjection.Take(Select(SqlQuery.OrderBy(Items, nameof(Item.Id)), nameof(Item.Name)), 5), 1);

        Assert.Equal(
            "SELECT [t1].[Name] FROM (SELECT TOP(@p0) [t0].[Name], [t0].[Id] FROM [shop].[Items] AS [t0] ORDER BY [t0].[Id]) AS [t1] " +
            "ORDER BY [t1].[Id] OFFSET @p1 ROWS",
            Projected(query));
    }

    [Fact]
    public void Distinct_SelectsDistinctProjectedRows()
        => Assert.Equal(
            "SELECT DISTINCT [t0].[Name] FROM [shop].[Items] AS [t0]",
            Projected(SqlProjection.Distinct(Select(Items, nameof(Item.Name)))));

    [Fact]
    public void Distinct_DropsOrderingBeforeIt()
        => Assert.Equal(
            "SELECT DISTINCT [t0].[Name] FROM [shop].[Items] AS [t0]",
            Projected(SqlProjection.Distinct(Select(SqlQuery.OrderBy(Items, nameof(Item.Score)), nameof(Item.Name)))));

    [Fact]
    public void TakeAfterDistinct_PagesTheDistinctRows()
        => Assert.Equal(
            "SELECT TOP(@p0) [t1].[Name] FROM (SELECT DISTINCT [t0].[Name] FROM [shop].[Items] AS [t0]) AS [t1]",
            Projected(SqlProjection.Take(SqlProjection.Distinct(Select(Items, nameof(Item.Name))), 2)));

    [Fact]
    public void DistinctAfterTake_IsDistinctOfThePage()
        => Assert.Equal(
            "SELECT DISTINCT [t1].[Name] FROM (SELECT TOP(@p0) [t0].[Name] FROM [shop].[Items] AS [t0]) AS [t1]",
            Projected(SqlProjection.Distinct(SqlProjection.Take(Select(Items, nameof(Item.Name)), 2))));

    [Fact]
    public void RowLimitAfterDistinct_LimitsTheDistinctRows()
        => Assert.Equal(
            "SELECT TOP(1) [t1].[Name] FROM (SELECT DISTINCT [t0].[Name] FROM [shop].[Items] AS [t0]) AS [t1]",
            Projected(SqlProjection.Distinct(Select(Items, nameof(Item.Name))), rowLimit: 1));

    [Fact]
    public void Count_OfDistinctRows_CountsADerivedTable()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT DISTINCT [t0].[Name] FROM [shop].[Items] AS [t0]) AS [t1]",
            Projected(SqlProjection.Distinct(Select(Items, nameof(Item.Name))), QueryTerminal.Count));

    [Fact]
    public void Count_OfAProjection_CountsTheRows()
        => Assert.Equal(
            "SELECT COUNT(*) FROM [shop].[Items] AS [t0]",
            Projected(Select(Items, nameof(Item.Name)), QueryTerminal.Count));

    [Fact]
    public void Any_OfDistinctRows_ChecksTheDistinctRows()
        => Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT DISTINCT 1 FROM [shop].[Items] AS [t0]) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Projected(SqlProjection.Distinct(Select(Items, nameof(Item.Name))), QueryTerminal.Any));

    [Fact]
    public void EntityDistinct_SelectsDistinctEntityRows()
        => Assert.Equal("SELECT DISTINCT " + Columns0 + " FROM " + Table, Build(SqlQuery.Distinct(Items)).CommandText);

    [Fact]
    public void WhereAfterEntityDistinct_FiltersTheDistinctRows()
        => Assert.Equal(
            "SELECT " + Columns1 + " FROM (SELECT DISTINCT " + Columns0 + " FROM " + Table + ") AS [t1] WHERE [t1].[Id] > @p0",
            Build(Where(SqlQuery.Distinct(Items), IdAbove, 3)).CommandText);

    [Fact]
    public void OrderByAfterEntityDistinct_OrdersTheDistinctRows()
        => Assert.Equal(
            "SELECT DISTINCT " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name]",
            Build(SqlQuery.OrderBy(SqlQuery.Distinct(Items), nameof(Item.Name))).CommandText);

    [Fact]
    public void Select_UnknownProperty_Throws()
        => Assert.Throws<InvalidOperationException>(() => Projected(Select(Items, "Missing")));

    internal static IQueryable<string> Select(IQueryable<Item> source, params string[] propertyNames)
        => SqlQuery.Select(source, propertyNames, [], static (reader, ordinals, _) => reader.GetString(ordinals[0]));

    private string Projected<T>(IQueryable<T> query, QueryTerminal terminal = QueryTerminal.Rows, int? rowLimit = null)
        => SqlBuilder.Build(Entity, ((ProjectedQueryable<T>)query).Tail, SqlServerDialect.Instance, terminal, rowLimit).CommandText;
}
