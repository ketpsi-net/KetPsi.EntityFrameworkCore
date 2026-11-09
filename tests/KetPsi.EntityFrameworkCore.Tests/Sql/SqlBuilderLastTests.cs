using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderLastTests : SqlBuilderTestBase
{
    [Fact]
    public void Reverse_FlipsEveryOrderingAndTakesOneRow()
    {
        var query = SqlQuery.ThenByDescending(SqlQuery.OrderBy(Items, nameof(Item.Name)), nameof(Item.Id));

        Assert.Equal(
            "SELECT TOP(1) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name] DESC, [t0].[Id]",
            Last(query).CommandText);
    }

    [Fact]
    public void Reverse_AfterTake_ReadsThePageFromItsEnd()
    {
        var query = SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Name)), 5);

        Assert.Equal(
            "SELECT TOP(1) " + Columns1 + " FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Name]) AS [t1]" +
            " ORDER BY [t1].[Name] DESC",
            Last(query).CommandText);
    }

    [Fact]
    public void Reverse_AfterSkip_ReadsTheRestFromItsEnd()
    {
        var query = SqlQuery.Skip(SqlQuery.OrderByDescending(Items, nameof(Item.Score)), 2);

        Assert.Equal(
            "SELECT TOP(1) " + Columns1 + " FROM (SELECT " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Score] DESC OFFSET @p0 ROWS)" +
            " AS [t1] ORDER BY [t1].[Score]",
            Last(query).CommandText);
    }

    [Fact]
    public void Reverse_OfAProjection_KeepsTheEntityOrdering()
    {
        var query = SqlBuilderProjectionTests.Select(SqlQuery.OrderBy(Items, nameof(Item.Score)), nameof(Item.Name));
        var tail = new ReverseOrderNode(((ProjectedQueryable<string>)query).Tail, "Last");

        Assert.Equal(
            "SELECT TOP(1) [t0].[Name] FROM " + Table + " ORDER BY [t0].[Score] DESC",
            SqlBuilder.Build(Entity, tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: 1).CommandText);
    }

    [Theory]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    public void Reverse_WithoutOrdering_ThrowsLikeEfCore(string method)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Last(SqlQuery.Take(Items, 3), method));

        Assert.Equal(
            $"Queries performing '{method}' operation must have a deterministic sort order. " +
            $"Rewrite the query to apply an 'OrderBy' operation on the sequence before calling '{method}'.",
            exception.Message);
    }

    // Distinct rows have no order, so an ordering before Distinct leaves nothing to reverse, as in EF Core.
    [Fact]
    public void Reverse_AfterDistinct_Throws()
        => Assert.Throws<InvalidOperationException>(() => Last(SqlQuery.Distinct(SqlQuery.OrderBy(Items, nameof(Item.Name)))));

    private SqlStatement Last(IQueryable<Item> query, string method = "Last")
        => SqlBuilder.Build(
            Entity, new ReverseOrderNode(((SqlQueryable<Item>)query).Tail, method), SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: 1);
}
