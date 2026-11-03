using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderAggregateTests : SqlBuilderTestBase
{
    private static readonly ComparisonTemplate IdAbove = new(nameof(Item.Id), ComparisonOperator.GreaterThan, 0);
    private static readonly SqlAggregate SumScore = new(AggregateFunction.Sum, nameof(Item.Score), AggregateValueType.Int32);
    private static readonly SqlAggregate MaxOfProjection = new(AggregateFunction.Max, PropertyName: null, AggregateValueType.Column);

    [Fact]
    public void Aggregate_OfEntityProperty()
        => Assert.Equal("SELECT COALESCE(SUM([t0].[Score]), 0) FROM [shop].[Items] AS [t0]", Aggregate(Items, SumScore));

    [Fact]
    public void Aggregate_AfterWhere_AggregatesTheFilteredRows()
        => Assert.Equal(
            "SELECT MAX([t0].[Name]) FROM [shop].[Items] AS [t0] WHERE [t0].[Id] > @p0",
            Aggregate(Where(Items, IdAbove, 3), new SqlAggregate(AggregateFunction.Max, nameof(Item.Name), AggregateValueType.Column)));

    [Fact]
    public void Aggregate_IgnoresOrdering()
        => Assert.Equal(
            "SELECT COALESCE(SUM([t0].[Score]), 0) FROM [shop].[Items] AS [t0]",
            Aggregate(SqlQuery.OrderBy(Items, nameof(Item.Name)), SumScore));

    [Fact]
    public void Aggregate_AfterPaging_AggregatesThePage()
        => Assert.Equal(
            "SELECT COALESCE(SUM([t1].[Score]), 0) FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Id]) AS [t1]",
            Aggregate(SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Id)), 2), SumScore));

    [Fact]
    public void Aggregate_OfOneColumnProjection()
        => Assert.Equal(
            "SELECT MAX([t0].[Name]) FROM [shop].[Items] AS [t0]",
            Aggregate(SqlBuilderProjectionTests.Select(Items, nameof(Item.Name)), MaxOfProjection));

    [Fact]
    public void Aggregate_OfDistinctProjection_AggregatesTheDistinctRows()
        => Assert.Equal(
            "SELECT COALESCE(SUM([t1].[Score]), 0) FROM (SELECT DISTINCT [t0].[Score] FROM [shop].[Items] AS [t0]) AS [t1]",
            Aggregate(
                SqlProjection.Distinct(SqlBuilderProjectionTests.Select(Items, nameof(Item.Score))),
                new SqlAggregate(AggregateFunction.Sum, PropertyName: null, AggregateValueType.Int32)));

    [Fact]
    public void Aggregate_OfPagedProjection_KeepsTheColumn()
        => Assert.Equal(
            "SELECT MAX([t1].[Name]) FROM (SELECT TOP(@p0) [t0].[Name] FROM [shop].[Items] AS [t0]) AS [t1]",
            Aggregate(SqlProjection.Take(SqlBuilderProjectionTests.Select(Items, nameof(Item.Name)), 3), MaxOfProjection));

    [Fact]
    public void Aggregate_OfProjectionWithSeveralColumns_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => Aggregate(SqlBuilderProjectionTests.Select(Items, nameof(Item.Name), nameof(Item.Id)), MaxOfProjection));

    [Fact]
    public void Aggregate_OfEntityQueryWithoutProperty_Throws()
        => Assert.Throws<InvalidOperationException>(() => Aggregate(Items, MaxOfProjection));

    [Fact]
    public void AggregateTerminal_WithoutAggregate_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => SqlBuilder.Build(Entity, ((SqlQueryable<Item>)Items).Tail, SqlServerDialect.Instance, QueryTerminal.Aggregate));

    private string Aggregate<T>(IQueryable<T> query, SqlAggregate aggregate)
    {
        var tail = query switch
        {
            ProjectedQueryable<T> projected => projected.Tail,
            _ => ((SqlQueryable<Item>)(object)query).Tail,
        };

        return SqlBuilder.Build(Entity, tail, SqlServerDialect.Instance, QueryTerminal.Aggregate, aggregate: aggregate).CommandText;
    }
}
