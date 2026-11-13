using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>
/// Predicates and orderings on collection navigations, written as EF Core writes them on SQL Server (probed on EF Core
/// 9, 10 and 11): correlated subqueries that apply the related entity's query filters.
/// </summary>
public sealed class SqlBuilderCollectionTests : IDisposable
{
    private const string Topics = "FROM [Topics] AS [t1] WHERE [t1].[IsDeleted] = CAST(0 AS bit) AND [t0].[Id] = [t1].[ForumId]";

    private static readonly PredicateTemplate NotDeleted = new NotTemplate(new BooleanPropertyTemplate(nameof(Topic.IsDeleted)));
    private static readonly PredicateTemplate NotHidden = new NotTemplate(new BooleanPropertyTemplate(nameof(Reply.IsHidden)));

    private readonly ForumContext _db = new();

    public void Dispose() => _db.Dispose();

    // --- Any and All.

    [Fact]
    public void Any_IsExists_WithTheRelatedQueryFilters()
        => Assert.Equal("EXISTS (SELECT 1 " + Topics + ")", WhereSql(Exists("Topics")));

    [Fact]
    public void NotAny_IsNotExists()
        => Assert.Equal("NOT EXISTS (SELECT 1 " + Topics + ")", WhereSql(new NotTemplate(Exists("Topics"))));

    [Fact]
    public void AnyWithACondition_FiltersTheRelatedRows()
    {
        var statement = Build(SqlQuery.Where(Query(_db.Forums), Exists("Topics", new ComparisonTemplate(nameof(Topic.Score), ComparisonOperator.GreaterThan, 0)), [3]));

        Assert.EndsWith(" WHERE EXISTS (SELECT 1 " + Topics + " AND [t1].[Score] > @p0)", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal(nameof(Topic.Score), Assert.Single(statement.Parameters).Property!.Name);
    }

    [Fact]
    public void AnyAndAnotherCondition_NumberTheirParametersInOrder()
        => Assert.Equal(
            "(EXISTS (SELECT 1 " + Topics + " AND [t1].[Score] > @p0) AND [t0].[Name] <> @p1)",
            WhereSql(
                new AndTemplate(
                    Exists("Topics", new ComparisonTemplate(nameof(Topic.Score), ComparisonOperator.GreaterThan, 0)),
                    new ComparisonTemplate(nameof(Forum.Name), ComparisonOperator.NotEqual, 1)),
                3,
                "Beta"));

    [Fact]
    public void All_IsNotExistsOfTheNegation_WhichKeepsNullsLikeCSharp()
        => Assert.Equal(
            "NOT EXISTS (SELECT 1 " + Topics + " AND ([t1].[Rating] <= @p0 OR [t1].[Rating] IS NULL))",
            WhereSql(new NotTemplate(Exists("Topics", new NotTemplate(new ComparisonTemplate(nameof(Topic.Rating), ComparisonOperator.GreaterThan, 0)))), 1m));

    [Fact]
    public void AnInnerName_ReadsTheOuterRow()
        => Assert.Equal(
            "EXISTS (SELECT 1 " + Topics + " AND [t1].[Score] >= [t0].[MinScore])",
            WhereSql(Exists("Topics", Compare(P(nameof(Topic.Score)), ComparisonOperator.GreaterThanOrEqual, P("^" + nameof(Forum.MinScore))))));

    [Fact]
    public void AnyUsedAsABoolean_IsACase()
        => Assert.Equal(
            "CASE WHEN EXISTS (SELECT 1 " + Topics + ") THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = CASE WHEN [t0].[MinScore] > @p0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            WhereSql(
                Compare(
                    new PredicateValueTemplate(Exists("Topics")),
                    ComparisonOperator.Equal,
                    new PredicateValueTemplate(new ComparisonTemplate(nameof(Forum.MinScore), ComparisonOperator.GreaterThan, 0))),
                2));

    [Fact]
    public void WithoutQueryFilters_OnlyTheKeysMatch()
        => Assert.Equal(
            "EXISTS (SELECT 1 FROM [Topics] AS [t1] WHERE [t0].[Id] = [t1].[ForumId])",
            WhereSql(Exists("Topics"), filtered: false));

    // --- Navigations around the collection.

    [Fact]
    public void ReferenceInside_IsJoinedInTheSubquery()
        => Assert.Equal(
            "EXISTS (SELECT 1 FROM [Topics] AS [t1] INNER JOIN [Members] AS [t2] ON [t1].[StarterId] = [t2].[Id] " +
            "WHERE [t1].[IsDeleted] = CAST(0 AS bit) AND [t0].[Id] = [t1].[ForumId] AND [t2].[Name] = @p0)",
            WhereSql(Exists("Topics", new ComparisonTemplate("Starter.Name", ComparisonOperator.Equal, 0)), "Bob"));

    [Fact]
    public void OptionalReferenceInside_IsLeftJoined()
        => Assert.Equal(
            "EXISTS (SELECT 1 FROM [Topics] AS [t1] LEFT JOIN [Members] AS [t2] ON [t1].[ModeratorId] = [t2].[Id] " +
            "WHERE [t1].[IsDeleted] = CAST(0 AS bit) AND [t0].[Id] = [t1].[ForumId] AND [t2].[Name] = @p0)",
            WhereSql(Exists("Topics", new ComparisonTemplate("Moderator.Name", ComparisonOperator.Equal, 0)), "Ann"));

    [Fact]
    public void MissingReferenceInside_ReadsTheForeignKey()
        => Assert.Equal(
            "EXISTS (SELECT 1 " + Topics + " AND [t1].[ModeratorId] IS NULL)",
            WhereSql(Exists("Topics", new NullCheckTemplate("Moderator", isNull: true))));

    [Fact]
    public void CollectionThroughAReference_JoinsTheReferenceOutside()
        => Assert.Equal(
            "SELECT " + Cols<Topic>("t0") + " FROM [Topics] AS [t0] INNER JOIN [Forums] AS [t1] ON [t0].[ForumId] = [t1].[Id] " +
            "WHERE (SELECT COUNT(*) FROM [Topics] AS [t2] WHERE [t2].[IsDeleted] = CAST(0 AS bit) AND [t1].[Id] = [t2].[ForumId]) > @p0",
            Build(SqlQuery.Where(Query(_db.Topics), Compare(new CountValueTemplate(new CollectionTemplate("Forum.Topics")), ComparisonOperator.GreaterThan, S(0)), [1])).CommandText);

    [Fact]
    public void AlternateKey_MatchesThePrincipalKey()
        => Assert.Equal("EXISTS (SELECT 1 FROM [Follower] AS [t1] WHERE [t0].[Code] = [t1].[ForumCode])", WhereSql(Exists("Followers")));

    [Fact]
    public void InverseOfAnOptionalReference_MatchesItsForeignKey()
        => Assert.EndsWith(
            " WHERE EXISTS (SELECT 1 FROM [Topics] AS [t1] WHERE [t1].[IsDeleted] = CAST(0 AS bit) AND [t0].[Id] = [t1].[ModeratorId])",
            Build(SqlQuery.Where(Query(_db.Members), Exists(nameof(Member.Moderated)), [])).CommandText,
            StringComparison.Ordinal);

    [Fact]
    public void JoinEntity_JoinsItsTarget()
        => Assert.EndsWith(
            " WHERE EXISTS (SELECT 1 FROM [TopicLabel] AS [t1] INNER JOIN [Label] AS [t2] ON [t1].[LabelId] = [t2].[Id] " +
            "WHERE [t0].[Id] = [t1].[TopicId] AND [t2].[Name] = @p0)",
            Build(SqlQuery.Where(Query(_db.Topics), Exists(nameof(Topic.TopicLabels), new ComparisonTemplate("Label.Name", ComparisonOperator.Equal, 0)), ["ef"]), filtered: false).CommandText,
            StringComparison.Ordinal);

    [Fact]
    public void NestedCollections_NestTheirSubqueries()
        => Assert.Equal(
            "EXISTS (SELECT 1 " + Topics + " AND EXISTS (SELECT 1 FROM [Reply] AS [t2] WHERE [t2].[IsHidden] = CAST(0 AS bit) " +
            "AND [t1].[Id] = [t2].[TopicId] AND [t2].[Likes] > @p0))",
            WhereSql(Exists("Topics", Exists(nameof(Topic.Replies), new ComparisonTemplate(nameof(Reply.Likes), ComparisonOperator.GreaterThan, 0))), 2));

    [Fact]
    public void NestedOuterNames_ReadEachRowOut()
        => Assert.Equal(
            "EXISTS (SELECT 1 " + Topics + " AND EXISTS (SELECT 1 FROM [Reply] AS [t2] WHERE [t2].[IsHidden] = CAST(0 AS bit) " +
            "AND [t1].[Id] = [t2].[TopicId] AND ([t2].[Likes] > [t1].[Score] AND [t2].[Likes] > [t0].[MinScore])))",
            WhereSql(Exists(
                "Topics",
                Exists(
                    nameof(Topic.Replies),
                    new AndTemplate(
                        Compare(P(nameof(Reply.Likes)), ComparisonOperator.GreaterThan, P("^" + nameof(Topic.Score))),
                        Compare(P(nameof(Reply.Likes)), ComparisonOperator.GreaterThan, P("^^" + nameof(Forum.MinScore))))))));

    [Fact]
    public void AfterPaging_TheSubqueryReadsThePagedRows()
        => Assert.Equal(
            "SELECT " + Cols<Forum>("t1") + " FROM (SELECT TOP(@p0) " + Cols<Forum>("t0") + " FROM [Forums] AS [t0] ORDER BY [t0].[Id]) AS [t1] " +
            "WHERE EXISTS (SELECT 1 FROM [Topics] AS [t2] WHERE [t2].[IsDeleted] = CAST(0 AS bit) AND [t1].[Id] = [t2].[ForumId]) ORDER BY [t1].[Id]",
            Build(SqlQuery.Where(SqlQuery.Take(SqlQuery.OrderBy(Query(_db.Forums), nameof(Forum.Id)), 2), Exists("Topics"), [])).CommandText);

    // --- Counts and aggregates.

    [Fact]
    public void Count_IsACorrelatedCount()
        => Assert.Equal("(SELECT COUNT(*) " + Topics + ") > @p0", WhereSql(Compare(Count("Topics"), ComparisonOperator.GreaterThan, S(0)), 1));

    [Fact]
    public void LongCount_IsCountBig()
        => Assert.Equal(
            "(SELECT COUNT_BIG(*) " + Topics + ") >= @p0",
            WhereSql(Compare(new CountValueTemplate(new CollectionTemplate("Topics"), isLong: true), ComparisonOperator.GreaterThanOrEqual, S(0)), 1L));

    [Fact]
    public void CountWithACondition_FiltersTheRelatedRows()
        => Assert.Equal(
            "(SELECT COUNT(*) " + Topics + " AND [t1].[Score] = @p0) = @p1",
            WhereSql(Compare(Count("Topics", new ComparisonTemplate(nameof(Topic.Score), ComparisonOperator.Equal, 0)), ComparisonOperator.Equal, S(1)), 4, 2));

    [Fact]
    public void Counts_AreNeverNull()
        => Assert.Equal(
            "(SELECT COUNT(*) " + Topics + ") = (SELECT COUNT(*) FROM [Follower] AS [t2] WHERE [t0].[Code] = [t2].[ForumCode])",
            WhereSql(Compare(Count("Topics"), ComparisonOperator.Equal, Count("Followers"))));

    [Fact]
    public void CountInArithmetic_NeedsNoParentheses()
        => Assert.Equal(
            "(SELECT COUNT(*) " + Topics + ") * @p0 > @p1",
            WhereSql(Compare(new BinaryValueTemplate(ValueOperator.Multiply, Count("Topics"), S(0)), ComparisonOperator.GreaterThan, S(1)), 2, 3));

    [Fact]
    public void Sum_IsCoalescedToZero()
    {
        Assert.Equal(
            "(SELECT COALESCE(SUM([t1].[Score]), 0) " + Topics + ") = @p0",
            WhereSql(Compare(Aggregate(AggregateFunction.Sum, nameof(Topic.Score), AggregateValueType.Int32), ComparisonOperator.Equal, S(0)), 0));
        Assert.Equal(
            "(SELECT COALESCE(SUM([t1].[Rating]), 0.0) " + Topics + ") > @p0",
            WhereSql(Compare(Aggregate(AggregateFunction.Sum, nameof(Topic.Rating), AggregateValueType.Decimal), ComparisonOperator.GreaterThan, S(0)), 3m));
    }

    [Fact]
    public void SumOfAComputedValue_ComputesEachRow()
        => Assert.Equal(
            "(SELECT COALESCE(SUM([t1].[Score] * @p0), 0) " + Topics + ") > @p1",
            WhereSql(
                Compare(
                    new CollectionAggregateTemplate(
                        new CollectionTemplate("Topics"), AggregateFunction.Sum,
                        new BinaryValueTemplate(ValueOperator.Multiply, P(nameof(Topic.Score)), S(0)), AggregateValueType.Int32),
                    ComparisonOperator.GreaterThan,
                    S(1)),
                2,
                10));

    [Fact]
    public void MaxOfNoRows_IsNull_SoNotEqualKeepsIt()
    {
        const string Max = "(SELECT MAX([t1].[Score]) " + Topics + ")";
        Assert.Equal(
            "(" + Max + " <> @p0 OR " + Max + " IS NULL)",
            WhereSql(Compare(Aggregate(AggregateFunction.Max, nameof(Topic.Score), AggregateValueType.Column), ComparisonOperator.NotEqual, S(0)), 4));
    }

    [Fact]
    public void MinComparedWithANullableColumn_IsFalseForNull()
        => Assert.Equal(
            "(SELECT MIN([t1].[Score]) " + Topics + ") >= [t0].[MinScore]",
            WhereSql(Compare(Aggregate(AggregateFunction.Min, nameof(Topic.Score), AggregateValueType.Column), ComparisonOperator.GreaterThanOrEqual, P(nameof(Forum.MinScore)))));

    [Fact]
    public void AverageOfIntegers_IsAFloat()
        => Assert.Equal(
            "(SELECT AVG(CAST([t1].[Score] AS float)) " + Topics + ") > @p0",
            WhereSql(Compare(Aggregate(AggregateFunction.Average, nameof(Topic.Score), AggregateValueType.Int32), ComparisonOperator.GreaterThan, S(0)), 3.0));

    [Fact]
    public void AggregateOfAReferenceInside_IsJoinedInTheSubquery()
        => Assert.Equal(
            "(SELECT MAX([t2].[Name]) FROM [Topics] AS [t1] INNER JOIN [Members] AS [t2] ON [t1].[StarterId] = [t2].[Id] " +
            "WHERE [t1].[IsDeleted] = CAST(0 AS bit) AND [t0].[Id] = [t1].[ForumId]) = @p0",
            WhereSql(Compare(Aggregate(AggregateFunction.Max, "Starter.Name", AggregateValueType.Column), ComparisonOperator.Equal, S(0)), "Bob"));

    // --- Orderings.

    [Fact]
    public void OrderByCount_OrdersByTheSubquery()
        => Assert.Equal(
            "SELECT " + Cols<Forum>("t0") + " FROM [Forums] AS [t0] ORDER BY (SELECT COUNT(*) " + Topics + ") DESC",
            Build(SqlQuery.OrderByDescending(Query(_db.Forums), Count("Topics"), [])).CommandText);

    [Fact]
    public void OrderByAny_IsACase()
        => Assert.Equal(
            "SELECT " + Cols<Forum>("t0") + " FROM [Forums] AS [t0] ORDER BY CASE WHEN EXISTS (SELECT 1 " + Topics + ") THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Build(SqlQuery.OrderBy(Query(_db.Forums), new PredicateValueTemplate(Exists("Topics")), [])).CommandText);

    [Fact]
    public void PagedOrderByCount_IsSelectedByADerivedTable()
        => Assert.Equal(
            "SELECT " + Cols<Forum>("t2") + " FROM (SELECT " + Cols<Forum>("t0") + ", (SELECT COUNT(*) " + Topics + ") AS [c] FROM [Forums] AS [t0] " +
            "ORDER BY (SELECT COUNT(*) " + Topics + ") DESC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY) AS [t2] WHERE [t2].[Id] <> @p2 ORDER BY [t2].[c] DESC",
            Build(SqlQuery.Where(
                SqlQuery.Take(SqlQuery.Skip(SqlQuery.OrderByDescending(Query(_db.Forums), Count("Topics"), []), 1), 2),
                new ComparisonTemplate(nameof(Forum.Id), ComparisonOperator.NotEqual, 0),
                [0])).CommandText);

    // --- Errors.

    [Fact]
    public void ReferenceNavigation_IsNotACollection()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(SqlQuery.Where(Query(_db.Topics), Exists(nameof(Topic.Forum)), [])));

        Assert.Contains("'KetPsi.EntityFrameworkCore.Tests.TestModel.Topic.Forum' is not a collection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OuterNameOutsideACollection_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => WhereSql(Compare(P("^" + nameof(Forum.MinScore)), ComparisonOperator.GreaterThan, S(0)), 1));

    [Fact]
    public void CollectionStartingOutside_IsNotSupported()
        => Assert.Throws<NotSupportedException>(
            () => WhereSql(Exists("Topics", Exists("^Followers"))));

    [Fact]
    public void CollectionInAJoinedQuery_IsNotSupported()
        => Assert.Throws<NotSupportedException>(
            () => Build(((ProjectedQueryable<int>)SqlQuery.Join(
                Query(_db.Topics),
                SqlQuery.Where(Query(_db.Forums), Exists("Topics"), []),
                ["ForumId"],
                ["Id"],
                ["Title"],
                [],
                static (_, _, _) => 0)).Tail));

    // --- Templates.

    [Fact]
    public void Templates_CountTheSlotsTheyRead()
    {
        var collection = new CollectionTemplate("Topics", new ComparisonTemplate(nameof(Topic.Score), ComparisonOperator.Equal, 2));

        Assert.Equal(3, collection.ValueCount);
        Assert.Equal(3, new ExistsTemplate(collection).ValueCount);
        Assert.Equal(3, new CountValueTemplate(collection).ValueCount);
        Assert.Equal(5, new CollectionAggregateTemplate(collection, AggregateFunction.Sum, S(4), AggregateValueType.Int32).ValueCount);
        Assert.Equal(0, new CollectionTemplate("Topics").ValueCount);
        Assert.Throws<ArgumentException>(() => new CollectionTemplate(""));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CollectionAggregateTemplate(collection, (AggregateFunction)9, S(0), AggregateValueType.Int32));
    }

    private static PropertyValueTemplate P(string name) => new(name);

    private static SlotValueTemplate S(int index) => new(index);

    private static ValueComparisonTemplate Compare(ValueTemplate left, ComparisonOperator comparison, ValueTemplate right) => new(left, comparison, right);

    private static ExistsTemplate Exists(string navigation, PredicateTemplate? predicate = null) => new(new CollectionTemplate(navigation, predicate));

    private static CountValueTemplate Count(string navigation, PredicateTemplate? predicate = null) => new(new CollectionTemplate(navigation, predicate));

    private static CollectionAggregateTemplate Aggregate(AggregateFunction function, string property, AggregateValueType valueType)
        => new(new CollectionTemplate("Topics"), function, P(property), valueType);

    private static IQueryable<T> Query<T>(IQueryable<T> set)
        where T : class
        => SqlQuery.ToSqlQueryable(set);

    // The WHERE clause the predicate builds on forums.
    private string WhereSql(PredicateTemplate predicate, params object?[] values) => WhereSql(predicate, filtered: true, values);

    private string WhereSql(PredicateTemplate predicate, bool filtered, params object?[] values)
    {
        var text = Build(SqlQuery.Where(Query(_db.Forums), predicate, values), filtered).CommandText;
        var where = text.IndexOf(" WHERE ", StringComparison.Ordinal);
        return text[(where + " WHERE ".Length)..];
    }

    // Topics are filtered by IsDeleted and replies by IsHidden, as query filters of the model would; the queried
    // entity's own filters are left out, so only the subqueries apply them.
    private SqlStatement Build<T>(IQueryable<T> query, bool filtered = true)
        where T : class
        => Build(((SqlQueryable<T>)query).Tail, filtered);

    private SqlStatement Build(QueryNode tail, bool filtered = true)
    {
        var entity = BindType(RootType(tail));
        return SqlBuilder.Build(
            entity, tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: null, [], aggregate: null,
            filtered ? FiltersFor : static _ => [], BindType);
    }

    private static IReadOnlyList<ResolvedQueryFilter> FiltersFor(BoundEntity entity)
        => entity.ClrType == typeof(Topic) ? [new ResolvedQueryFilter(Key: null, NotDeleted, [])]
            : entity.ClrType == typeof(Reply) ? [new ResolvedQueryFilter(Key: null, NotHidden, [])]
            : [];

    private static Type RootType(QueryNode node)
    {
        while (node.Parent is not null)
        {
            node = node.Parent;
        }

        return ((RootNode)node).EntityType;
    }

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);

    private string Cols<T>(string alias) => string.Join(", ", BindType(typeof(T)).Properties.Select(p => $"[{alias}].[{p.ColumnName}]"));
}
