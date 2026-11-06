using System.Diagnostics;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>GroupBy with keys, aggregates, HAVING and ordering; the SQL follows the EF Core probe.</summary>
public sealed class SqlBuilderGroupTests : SqlBuilderTestBase, IDisposable
{
    private const string GroupByScore = " FROM " + Table + " GROUP BY [t0].[Score]";

    private static readonly ComparisonTemplate IdAbove = new(nameof(Item.Id), ComparisonOperator.GreaterThan, 0);
    private static readonly ComparisonTemplate OperandAbove = new("0", ComparisonOperator.GreaterThan, 0);
    private static readonly ComparisonTemplate OperandIsNot = new("0", ComparisonOperator.NotEqual, 0);
    private static readonly GroupValue Key0 = GroupValue.Key(0);
    private static readonly GroupValue CountAll = GroupValue.Count(isLong: false);

    private readonly LibraryContext _library = new();

    [Fact]
    public void KeyAndCount_GroupByTheKeyColumn()
        => Assert.Equal(
            "SELECT [t0].[Score], COUNT(*)" + GroupByScore,
            Sql(Select(ByScore(), Key0, CountAll)));

    [Fact]
    public void Aggregates_UseEfCoreSql()
        => Assert.Equal(
            "SELECT COALESCE(SUM([t0].[Score]), 0), AVG(CAST([t0].[Id] AS float)), MIN([t0].[Name]), MAX([t0].[Score]), COUNT_BIG(*)" + GroupByScore,
            Sql(Select(
                ByScore(),
                GroupValue.Aggregate(AggregateFunction.Sum, nameof(Item.Score), AggregateValueType.Int32),
                GroupValue.Aggregate(AggregateFunction.Average, nameof(Item.Id), AggregateValueType.Int32),
                GroupValue.Aggregate(AggregateFunction.Min, nameof(Item.Name), AggregateValueType.Column),
                GroupValue.Aggregate(AggregateFunction.Max, nameof(Item.Score), AggregateValueType.Column),
                GroupValue.Count(isLong: true))));

    [Fact]
    public void CompositeKey_GroupsByEveryPart()
        => Assert.Equal(
            "SELECT [t0].[Score], [t0].[IsActive], COUNT(*) FROM " + Table + " GROUP BY [t0].[IsActive], [t0].[Score]",
            Sql(Select(GroupBy(Items, nameof(Item.IsActive), nameof(Item.Score)), GroupValue.Key(1), Key0, CountAll)));

    [Fact]
    public void CountWithPredicate_CountsTheMatchingRows()
        => Assert.Equal(
            "SELECT COUNT(CASE WHEN [t0].[Id] > @p0 THEN 1 END), COUNT_BIG(CASE WHEN [t0].[Id] > @p1 THEN 1 END)" + GroupByScore,
            Sql(SqlGrouping.Select(
                ByScore(),
                [GroupValue.Count(isLong: false, IdAbove), GroupValue.Count(isLong: true, IdAbove)],
                [3],
                Reader)));

    [Fact]
    public void DistinctCount_CountsDistinctValues()
        => Assert.Equal(
            "SELECT COUNT(DISTINCT ([t0].[Name])), COUNT_BIG(DISTINCT ([t0].[Name]))" + GroupByScore,
            Sql(Select(ByScore(), GroupValue.DistinctCount(nameof(Item.Name), isLong: false), GroupValue.DistinctCount(nameof(Item.Name), isLong: true))));

    [Fact]
    public void WhereBeforeGroupBy_FiltersTheRows()
        => Assert.Equal(
            "SELECT [t0].[Score] FROM " + Table + " WHERE [t0].[Id] > @p0 GROUP BY [t0].[Score]",
            Sql(Select(GroupBy(Where(Items, IdAbove, 3), nameof(Item.Score)), Key0)));

    [Fact]
    public void OrderingBeforeGroupBy_IsDropped()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore,
            Sql(Select(GroupBy(SqlQuery.OrderBy(Items, nameof(Item.Name)), nameof(Item.Score)), Key0)));

    [Fact]
    public void TakeBeforeGroupBy_GroupsTheRowsOfADerivedTable()
        => Assert.Equal(
            "SELECT [t1].[IsActive], COUNT(*) FROM (SELECT TOP(@p0) " + Columns0 + " FROM " + Table + " ORDER BY [t0].[Id]) AS [t1] " +
            "GROUP BY [t1].[IsActive]",
            Sql(Select(GroupBy(SqlQuery.Take(SqlQuery.OrderBy(Items, nameof(Item.Id)), 3), nameof(Item.IsActive)), Key0, CountAll)));

    [Fact]
    public void WhereOnGroups_IsHaving()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING COUNT(*) > @p0",
            Sql(Select(SqlGrouping.Where(ByScore(), OperandAbove, [CountAll], [1]), Key0)));

    [Fact]
    public void WhereAfterSelectOnGroups_IsHaving()
        => Assert.Equal(
            "SELECT [t0].[Score], COUNT(*)" + GroupByScore + " HAVING COUNT(*) > @p0",
            Sql(SqlProjection.Where(Select(ByScore(), Key0, CountAll), OperandAbove, [CountAll], [1])));

    [Fact]
    public void Having_OnANullableKey_FollowsCSharpNullSemantics()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING ([t0].[Score] <> @p0 OR [t0].[Score] IS NULL)",
            Sql(Select(SqlGrouping.Where(ByScore(), OperandIsNot, [Key0], [5]), Key0)));

    [Fact]
    public void Having_OnACount_NeverChecksForNull()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING COUNT(*) <> @p0",
            Sql(Select(SqlGrouping.Where(ByScore(), OperandIsNot, [CountAll], [5]), Key0)));

    [Fact]
    public void Having_OnMaxOfANullableColumn_ChecksForNull()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING (MAX([t0].[Score]) <> @p0 OR MAX([t0].[Score]) IS NULL)",
            Sql(Select(
                SqlGrouping.Where(ByScore(), OperandIsNot, [GroupValue.Aggregate(AggregateFunction.Max, nameof(Item.Score), AggregateValueType.Column)], [5]),
                Key0)));

    [Fact]
    public void Having_WithSeveralOperands_CombinesThem()
    {
        var predicate = new AndTemplate(OperandAbove, new ComparisonTemplate("1", ComparisonOperator.LessThan, 1));

        Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING (COUNT(*) > @p0 AND COALESCE(SUM([t0].[Id]), 0) < @p1)",
            Sql(Select(
                SqlGrouping.Where(ByScore(), predicate, [CountAll, GroupValue.Aggregate(AggregateFunction.Sum, nameof(Item.Id), AggregateValueType.Int32)], [1, 10]),
                Key0)));
    }

    [Fact]
    public void Having_ReadsTheOperandsCountPredicateValues()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " HAVING COUNT(CASE WHEN [t0].[Id] > @p0 THEN 1 END) > @p1",
            Sql(Select(
                SqlGrouping.Where(ByScore(), OperandAbove, [GroupValue.Count(isLong: false, new ComparisonTemplate(nameof(Item.Id), ComparisonOperator.GreaterThan, 1))], [2, 5]),
                Key0)));

    [Fact]
    public void Having_TypesItsParameterAsTheValue()
    {
        var statement = Statement(Select(SqlGrouping.Where(ByScore(), OperandAbove, [GroupValue.Count(isLong: true)], [1L]), Key0));

        Assert.Equal("bigint", Assert.Single(statement.Parameters).Property?.StoreType);
    }

    [Fact]
    public void Having_BooleanKey_ComparesWithTrue()
        => Assert.Equal(
            "SELECT [t0].[IsActive] FROM " + Table + " GROUP BY [t0].[IsActive] HAVING [t0].[IsActive] = CAST(1 AS bit)",
            Sql(Select(SqlGrouping.Where(GroupBy(Items, nameof(Item.IsActive)), new BooleanPropertyTemplate("0"), [Key0], []), Key0)));

    [Fact]
    public void Having_UnknownOperand_Throws()
        => Assert.Throws<ArgumentException>(() => Sql(Select(SqlGrouping.Where(ByScore(), new ComparisonTemplate("1", ComparisonOperator.Equal, 0), [Key0], [1]), Key0)));

    [Fact]
    public void OrderByAggregateThenKey_OrdersTheGroups()
        => Assert.Equal(
            "SELECT [t0].[Score], COUNT(*)" + GroupByScore + " ORDER BY COUNT(*) DESC, [t0].[Score]",
            Sql(Select(SqlGrouping.ThenBy(SqlGrouping.OrderByDescending(ByScore(), CountAll, []), Key0, []), Key0, CountAll)));

    [Fact]
    public void OrderByAfterSelectOnGroups_OrdersTheGroups()
        => Assert.Equal(
            "SELECT [t0].[Score], COUNT(*)" + GroupByScore + " ORDER BY COUNT(*), [t0].[Score] DESC",
            Sql(SqlProjection.ThenByDescending(SqlProjection.OrderBy(Select(ByScore(), Key0, CountAll), CountAll, []), Key0, [])));

    [Fact]
    public void OrderBy_ReplacesTheOrderingBeforeIt()
        => Assert.Equal(
            "SELECT [t0].[Score]" + GroupByScore + " ORDER BY [t0].[Score]",
            Sql(SqlProjection.OrderBy(Select(SqlGrouping.OrderBy(ByScore(), CountAll, []), Key0), Key0, [])));

    [Fact]
    public void PagingAfterSelect_PagesTheGroups()
        => Assert.Equal(
            "SELECT [t0].[Score], COUNT(*)" + GroupByScore + " ORDER BY [t0].[Score] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY",
            Sql(SqlProjection.Take(SqlProjection.Skip(SqlProjection.OrderBy(Select(ByScore(), Key0, CountAll), Key0, []), 1), 2)));

    [Fact]
    public void First_LimitsTheGroups()
        => Assert.Equal(
            "SELECT TOP(1) [t0].[Score], COUNT(*)" + GroupByScore + " ORDER BY [t0].[Score]",
            Sql(SqlProjection.OrderBy(Select(ByScore(), Key0, CountAll), Key0, []), rowLimit: 1));

    [Fact]
    public void HavingAfterPaging_IsRejected()
        => Assert.Throws<UnreachableException>(() => Sql(SqlProjection.Where(SqlProjection.Take(Select(ByScore(), Key0, CountAll), 2), OperandAbove, [CountAll], [1])));

    [Fact]
    public void WhereOnEntitiesAfterGroupBy_IsRejected()
        => Assert.Throws<UnreachableException>(() => Statement(new WhereNode(Tail(ByScore()), IdAbove, [1])));

    [Fact]
    public void CountOfGroups_CountsADerivedTableOfTheKeys()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT [t0].[Score]" + GroupByScore + ") AS [t1]",
            Sql(ByScore(), QueryTerminal.Count));

    [Fact]
    public void LongCountOfGroupsAfterSelect_CountsADerivedTableOfTheValues()
        => Assert.Equal(
            "SELECT COUNT_BIG(*) FROM (SELECT COUNT(*) AS [c], MAX([t0].[Id]) AS [c0]" + GroupByScore + ") AS [t1]",
            Sql(Select(ByScore(), CountAll, GroupValue.Aggregate(AggregateFunction.Max, nameof(Item.Id), AggregateValueType.Column)), QueryTerminal.LongCount));

    [Fact]
    public void AnyGroup_ChecksTheGroupsWithHaving()
        => Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1" + GroupByScore + " HAVING COUNT(*) > @p0) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Sql(SqlGrouping.Where(ByScore(), OperandAbove, [CountAll], [1]), QueryTerminal.Any));

    [Fact]
    public void DistinctAfterSelect_SelectsDistinctValues()
        => Assert.Equal(
            "SELECT DISTINCT COUNT(*)" + GroupByScore,
            Sql(SqlProjection.Distinct(Select(ByScore(), CountAll))));

    [Fact]
    public void AggregateOfGroups_AggregatesADerivedTable()
        => Assert.Equal(
            "SELECT MAX([t1].[c]) FROM (SELECT COUNT(*) AS [c]" + GroupByScore + ") AS [t1]",
            Sql(Select(ByScore(), CountAll), QueryTerminal.Aggregate, aggregate: new SqlAggregate(AggregateFunction.Max, PropertyName: null, AggregateValueType.Column)));

    [Fact]
    public void KeyIndexOutOfRange_Throws()
        => Assert.Throws<ArgumentException>(() => Sql(Select(ByScore(), GroupValue.Key(1))));

    [Fact]
    public void RowsOfGroupsWithoutSelect_AreNotSupportedYet()
        => Assert.Throws<NotSupportedException>(() => Sql(ByScore()));

    [Fact]
    public void KeyThroughRequiredReference_IsInnerJoined()
        => Assert.Equal(
            "SELECT [t1].[Name], COUNT(*) FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] GROUP BY [t1].[Name]",
            LibrarySql(Select(GroupBy(Books, "Writer.Name"), Key0, CountAll)));

    [Fact]
    public void KeyThroughOptionalReference_IsLeftJoinedAndNullable()
        => Assert.Equal(
            "SELECT [t1].[Id] FROM [Book] AS [t0] LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id] GROUP BY [t1].[Id] " +
            "HAVING ([t1].[Id] <> @p0 OR [t1].[Id] IS NULL)",
            LibrarySql(Select(SqlGrouping.Where(GroupBy(Books, "Editor.Id"), OperandIsNot, [Key0], [3]), Key0)));

    [Fact]
    public void CountOfGroupsWithRepeatedKeyNames_RenamesTheColumns()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT [t0].[Id], [t1].[Id] AS [Id0] FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] " +
            "GROUP BY [t0].[Id], [t1].[Id]) AS [t2]",
            LibrarySql(GroupBy(Books, "Id", "Writer.Id"), QueryTerminal.Count));

    [Fact]
    public void GroupBy_WithoutKeys_Throws()
        => Assert.Throws<ArgumentException>(() => GroupBy(Items));

    [Fact]
    public void AggregateThroughNavigation_Throws()
        => Assert.Throws<ArgumentException>(() => GroupValue.Aggregate(AggregateFunction.Max, "Writer.Id", AggregateValueType.Column));

    [Fact]
    public void Select_PredicateWithoutItsValues_Throws()
        => Assert.Throws<ArgumentException>(() => SqlGrouping.Select(ByScore(), [GroupValue.Count(isLong: false, IdAbove)], [], Reader));

    [Fact]
    public void IsInterceptable_OnlyForGroupedQueries()
    {
        Assert.True(SqlGrouping.IsInterceptable(ByScore()));
        Assert.False(SqlGrouping.IsInterceptable(Array.Empty<Item>().AsQueryable().GroupBy(static i => i.Score)));
    }

    public new void Dispose()
    {
        _library.Dispose();
        base.Dispose();
    }

    private static int Reader(System.Data.Common.DbDataReader reader, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities) => 0;

    private static IQueryable<IGrouping<object, T>> GroupBy<T>(IQueryable<T> source, params string[] keyPaths)
        where T : class
        => SqlGrouping.GroupBy<T, object, T>(source, keyPaths);

    private static IQueryable<int> Select<T>(IQueryable<IGrouping<object, T>> source, params GroupValue[] values)
        => SqlGrouping.Select(source, values, [], Reader);

    private static QueryNode Tail<T>(IQueryable<T> query) => query switch
    {
        ProjectedQueryable<T> projected => projected.Tail,
        _ => (QueryNode)query.GetType().GetProperty(nameof(ProjectedQueryable<T>.Tail))!.GetValue(query)!,
    };

    private IQueryable<IGrouping<object, Item>> ByScore() => GroupBy(Items, nameof(Item.Score));

    private IQueryable<Book> Books => SqlQuery.ToSqlQueryable(_library.Set<Book>());

    private string Sql<T>(IQueryable<T> query, QueryTerminal terminal = QueryTerminal.Rows, int? rowLimit = null, SqlAggregate? aggregate = null)
        => SqlBuilder.Build(Entity, Tail(query), SqlServerDialect.Instance, terminal, rowLimit, aggregate: aggregate).CommandText;

    private SqlStatement Statement<T>(IQueryable<T> query) => SqlBuilder.Build(Entity, Tail(query), SqlServerDialect.Instance, QueryTerminal.Rows);

    // A chain the generator never emits, to check the builder rejects it.
    private SqlStatement Statement(QueryNode tail) => SqlBuilder.Build(Entity, tail, SqlServerDialect.Instance, QueryTerminal.Rows);

    private string LibrarySql<T>(IQueryable<T> query, QueryTerminal terminal = QueryTerminal.Rows)
    {
        BoundEntity Bind(Type type) => ModelBinder.GetEntity(_library.Model, type);
        return SqlBuilder.Build(Bind(typeof(Book)), Tail(query), SqlServerDialect.Instance, terminal, bindEntity: Bind).CommandText;
    }
}
