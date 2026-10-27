using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.Binding;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderShapeTests : SqlBuilderTestBase
{
    private static readonly ComparisonTemplate IdEquals = new(nameof(Item.Id), ComparisonOperator.Equal, 0);

    [Fact]
    public void Root_SelectsEveryColumnFromQualifiedTable()
    {
        var statement = Build(Items);

        Assert.Equal(SelectAll, statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public void TrackingAndFilterNodes_DoNotChangeSql()
        => Assert.Equal(SelectAll, Build(SqlQuery.IgnoreQueryFilters(SqlQuery.AsNoTracking(Items))).CommandText);

    [Fact]
    public void OrderByThenByDescending_RendersOrderByList()
    {
        var query = SqlQuery.ThenByDescending(SqlQuery.OrderBy(Items, nameof(Item.Name)), nameof(Item.Id));

        Assert.Equal(SelectAll + " ORDER BY [t0].[Name], [t0].[Id] DESC", Build(query).CommandText);
    }

    [Fact]
    public void OrderByDescending_RendersDesc()
        => Assert.Equal(
            SelectAll + " ORDER BY [t0].[Score] DESC",
            Build(SqlQuery.OrderByDescending(Items, nameof(Item.Score))).CommandText);

    [Fact]
    public void SecondOrderBy_ReplacesOrdering()
    {
        var query = SqlQuery.OrderBy(SqlQuery.OrderBy(Items, nameof(Item.Name)), nameof(Item.Id));

        Assert.Equal(SelectAll + " ORDER BY [t0].[Id]", Build(query).CommandText);
    }

    [Fact]
    public void OrderBy_UnknownProperty_Throws()
        => Assert.Throws<InvalidOperationException>(() => Build(SqlQuery.OrderBy(Items, "Missing")));

    [Fact]
    public void Take_UsesTopWithParameter()
    {
        var statement = Build(SqlQuery.Take(Items, 10));

        Assert.Equal("SELECT TOP(@p0) " + Columns0 + " FROM " + Table, statement.CommandText);
        Assert.Equal([new QueryParameter("@p0", 10, Property: null)], statement.Parameters);
    }

    [Fact]
    public void Skip_WithoutOrdering_UsesFallbackOrder()
        => Assert.Equal(SelectAll + " ORDER BY (SELECT 1) OFFSET @p0 ROWS", Build(SqlQuery.Skip(Items, 5)).CommandText);

    [Fact]
    public void SkipThenTake_UsesOffsetFetchWithoutWrapping()
    {
        var query = SqlQuery.Take(SqlQuery.Skip(SqlQuery.OrderBy(Items, nameof(Item.Name)), 5), 10);

        var statement = Build(query);

        Assert.Equal(SelectAll + " ORDER BY [t0].[Name] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY", statement.CommandText);
        Assert.Equal([5, 10], statement.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void WhereAfterTake_FiltersPagedRowsAndKeepsOrdering()
    {
        var query = Where(SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Name)), 10), IdEquals, 3);

        var statement = Build(query);

        Assert.Equal(
            "SELECT " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name]) AS [t1]" +
            " WHERE [t1].[Id] = @p1 ORDER BY [t1].[Name]",
            statement.CommandText);
        Assert.Equal(
            [new QueryParameter("@p0", 10, Property: null), new QueryParameter("@p1", 3, Property(nameof(Item.Id)))],
            statement.Parameters);
    }

    [Fact]
    public void WhereBeforeTake_StaysInOneSelect()
        => Assert.Equal(
            "SELECT TOP(@p1) " + Columns0 + " FROM " + Table + " WHERE [t0].[Id] = @p0",
            Build(SqlQuery.Take(Where(Items, IdEquals, 3), 10)).CommandText);

    [Fact]
    public void TakeAfterTake_Wraps()
        => Assert.Equal(
            "SELECT TOP(@p1) " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + ") AS [t1]",
            Build(SqlQuery.Take(SqlQuery.Take(Items, 10), 5)).CommandText);

    [Fact]
    public void SkipAfterSkip_Wraps()
        => Assert.Equal(
            "SELECT " + Columns1 + " FROM (" + SelectAll + " ORDER BY (SELECT 1) OFFSET @p0 ROWS) AS [t1]" +
            " ORDER BY (SELECT 1) OFFSET @p1 ROWS",
            Build(SqlQuery.Skip(SqlQuery.Skip(Items, 5), 2)).CommandText);

    [Fact]
    public void SkipAfterTake_Wraps()
        => Assert.Equal(
            "SELECT " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + ") AS [t1]" +
            " ORDER BY (SELECT 1) OFFSET @p1 ROWS",
            Build(SqlQuery.Skip(SqlQuery.Take(Items, 10), 2)).CommandText);

    [Fact]
    public void OrderByAfterTake_WrapsAndReplacesOrdering()
    {
        var query = SqlQuery.OrderBy(SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Name)), 3), nameof(Item.Id));

        Assert.Equal(
            "SELECT " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name]) AS [t1]" +
            " ORDER BY [t1].[Id]",
            Build(query).CommandText);
    }

    [Fact]
    public void ThreeLevels_NumberAliasesAndParametersInOrder()
    {
        var query = Where(SqlQuery.Take(Where(SqlQuery.Take(Items, 10), IdEquals, 1), 5), IdEquals, 2);

        var statement = Build(query);

        Assert.Equal(
            "SELECT [t2].[Id], [t2].[IsActive], [t2].[Name], [t2].[Score] FROM (" +
            "SELECT TOP(@p2) " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + ") AS [t1]" +
            " WHERE [t1].[Id] = @p1) AS [t2] WHERE [t2].[Id] = @p3",
            statement.CommandText);
        Assert.Equal([10, 1, 5, 2], statement.Parameters.Select(p => p.Value));
    }

    // QueryTerminal is internal and xUnit test methods are public, so terminals are passed as int.
    [Theory]
    [InlineData((int)QueryTerminal.Count, "COUNT(*)")]
    [InlineData((int)QueryTerminal.LongCount, "COUNT_BIG(*)")]
    public void Count_SelectsAggregateWithoutOrdering(int terminal, string aggregate)
    {
        var query = SqlQuery.OrderBy(Where(Items, IdEquals, 1), nameof(Item.Name));

        Assert.Equal(
            "SELECT " + aggregate + " FROM " + Table + " WHERE [t0].[Id] = @p0",
            Build(query, (QueryTerminal)terminal).CommandText);
    }

    [Fact]
    public void Count_AfterTake_CountsPagedRows()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + ") AS [t1]",
            Build(SqlQuery.Take(Items, 10), QueryTerminal.Count).CommandText);

    [Fact]
    public void Count_AfterOrderedSkip_KeepsOrderingInsideDerivedTable()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (" + SelectAll + " ORDER BY [t0].[Name] OFFSET @p0 ROWS) AS [t1]",
            Build(SqlQuery.Skip(SqlQuery.OrderBy(Items, nameof(Item.Name)), 5), QueryTerminal.Count).CommandText);

    [Fact]
    public void Any_WrapsSelectInExistsWithoutOrdering()
        => Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM " + Table + " WHERE [t0].[Id] = @p0) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Build(SqlQuery.OrderBy(Where(Items, IdEquals, 1), nameof(Item.Name)), QueryTerminal.Any).CommandText);

    [Fact]
    public void Any_WithPaging_KeepsOrderingAndPaging()
        => Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT TOP(@p0) 1 FROM " + Table + " ORDER BY [t0].[Name]) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Build(SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Name)), 3), QueryTerminal.Any).CommandText);

    [Fact]
    public void RowLimit_UsesLiteralTop()
    {
        var statement = Build(SqlQuery.OrderBy(Items, nameof(Item.Name)), rowLimit: 1);

        Assert.Equal("SELECT TOP(1) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name]", statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public void RowLimit_AfterTake_Wraps()
        => Assert.Equal(
            "SELECT TOP(2) " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + ") AS [t1]",
            Build(SqlQuery.Take(Items, 10), rowLimit: 2).CommandText);

    [Fact]
    public void RowLimit_AfterSkip_UsesFetchNext()
        => Assert.Equal(
            SelectAll + " ORDER BY (SELECT 1) OFFSET @p0 ROWS FETCH NEXT 1 ROWS ONLY",
            Build(SqlQuery.Skip(Items, 5), rowLimit: 1).CommandText);

    [Theory]
    [InlineData((int)QueryTerminal.Count)]
    [InlineData((int)QueryTerminal.LongCount)]
    [InlineData((int)QueryTerminal.Any)]
    public void RowLimit_WithNonRowTerminal_Throws(int terminal)
        => Assert.Throws<ArgumentException>("rowLimit", () => Build(Items, (QueryTerminal)terminal, rowLimit: 1));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RowLimit_NotPositive_Throws(int rowLimit)
        => Assert.Throws<ArgumentOutOfRangeException>("rowLimit", () => Build(Items, rowLimit: rowLimit));

    [Fact]
    public void Build_EntityDoesNotMatchRoot_Throws()
    {
        var product = ModelBinderTests.Bind<ProductContext, Product>();
        var tail = ((SqlQueryable<Item>)Items).Tail;

        Assert.Throws<ArgumentException>(() => SqlBuilder.Build(product, tail, SqlServerDialect.Instance, QueryTerminal.Rows));
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var tail = ((SqlQueryable<Item>)Items).Tail;

        Assert.Throws<ArgumentNullException>("entity", () => SqlBuilder.Build(null!, tail, SqlServerDialect.Instance, QueryTerminal.Rows));
        Assert.Throws<ArgumentNullException>("tail", () => SqlBuilder.Build(Entity, null!, SqlServerDialect.Instance, QueryTerminal.Rows));
        Assert.Throws<ArgumentNullException>("dialect", () => SqlBuilder.Build(Entity, tail, null!, QueryTerminal.Rows));
    }

    [Fact]
    public void Build_SameChainTwice_ProducesSameStatement()
    {
        var query = Where(SqlQuery.Take(Items, 10), IdEquals, 3);

        var first = Build(query);
        var second = Build(query);

        Assert.Equal(first.CommandText, second.CommandText);
        Assert.Equal(first.Parameters, second.Parameters);
    }
}
