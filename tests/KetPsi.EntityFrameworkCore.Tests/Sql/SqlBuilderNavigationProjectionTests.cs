using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>Projections that read through navigations or select entities; the SQL follows the EF Core probe.</summary>
public sealed class SqlBuilderNavigationProjectionTests : IDisposable
{
    private const string WriterJoin = "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id]";
    private static readonly ComparisonTemplate NameIsNot = new(nameof(Writer.Name), ComparisonOperator.NotEqual, 0);

    private readonly LibraryContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ColumnThroughRequiredReference_IsInnerJoined()
        => Assert.Equal(
            "SELECT [t1].[Name] FROM [Book] AS [t0] " + WriterJoin,
            Sql(Select(Query<Book>(), ["Writer.Name"])));

    [Fact]
    public void ColumnThroughOptionalReference_IsLeftJoined()
        => Assert.Equal(
            "SELECT [t1].[Id] FROM [Book] AS [t0] LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id]",
            Sql(Select(Query<Book>(), ["Editor.Id"])));

    [Fact]
    public void RequiredReferenceAfterOptionalOne_IsLeftJoined()
        => Assert.Equal(
            "SELECT [t2].[Name] FROM [Review] AS [t0] LEFT JOIN [Book] AS [t1] ON [t0].[BookId] = [t1].[Id] " +
            "LEFT JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id]",
            Sql(Select(Query<Review>(), ["Book.Writer.Name"])));

    [Fact]
    public void ColumnsThroughTheSameNavigation_ShareOneJoin()
        => Assert.Equal(
            "SELECT [t1].[Name], [t0].[Id], [t1].[Id] FROM [Book] AS [t0] " + WriterJoin,
            Sql(Select(Query<Book>(), ["Writer.Name", "Id", "Writer.Id"])));

    [Fact]
    public void JoinedEntityFilters_AreInTheJoinCondition()
        => Assert.Equal(
            "SELECT [t1].[Name] FROM [Book] AS [t0] " + WriterJoin + " AND [t1].[Name] <> @p0",
            Sql(Select(Query<Book>(), ["Writer.Name"]), filtersFor: Filters(typeof(Writer), NameIsNot, "hidden")));

    [Fact]
    public void QueriedEntity_IsSelectedAfterTheColumns()
    {
        var result = Rows(Select(Query<Book>(), ["Writer.Name"], [""]));

        Assert.Equal($"SELECT [t1].[Name], {Cols<Book>("t0")} FROM [Book] AS [t0] " + WriterJoin, result.Statement.CommandText);
        Assert.Equal([new EntitySegment(Bind<Book>(), 1, null, null)], result.Segments);
        Assert.Equal([0], result.Roots);
    }

    [Fact]
    public void EntityThroughNavigation_IsJoined()
    {
        var result = Rows(Select(Query<Book>(), [], ["Writer"]));

        Assert.Equal($"SELECT {Cols<Writer>("t1")} FROM [Book] AS [t0] " + WriterJoin, result.Statement.CommandText);
        Assert.Equal([new EntitySegment(Bind<Writer>(), 0, null, null)], result.Segments);
    }

    [Fact]
    public void IncludesOfTheProjectedEntity_ShareTheProjectionsJoins()
    {
        var result = Rows(Select(Include(Query<Book>(), nameof(Book.Writer)), ["Writer.Name"], [""]));

        Assert.Equal(
            $"SELECT [t1].[Name], {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] " + WriterJoin,
            result.Statement.CommandText);
        Assert.Equal(
            [
                new EntitySegment(Bind<Book>(), 1, null, null),
                new EntitySegment(Bind<Writer>(), 1 + Bind<Book>().Properties.Count, 0, Bind<Book>().GetNavigation(nameof(Book.Writer))),
            ],
            result.Segments);
    }

    [Fact]
    public void IncludesBelowAProjectedNavigation_ApplyToItsEntity()
    {
        var result = Rows(Select(Include(Query<Book>(), nameof(Book.Writer), nameof(Writer.Profile)), [], ["Writer"]));

        Assert.Equal(
            $"SELECT {Cols<Writer>("t1")}, {Cols<WriterProfile>("t2")} FROM [Book] AS [t0] " + WriterJoin +
            " LEFT JOIN [WriterProfile] AS [t2] ON [t1].[Id] = [t2].[WriterId]",
            result.Statement.CommandText);
        Assert.Equal(0, result.Segments[1].Parent);
        Assert.Equal(nameof(Writer.Profile), result.Segments[1].Navigation!.Name);
    }

    [Fact]
    public void IncludesOfAnEntityNotProjected_AreIgnored()
    {
        var result = Rows(Select(Include(Query<Book>(), nameof(Book.Writer)), ["Id"]));

        Assert.Equal("SELECT [t0].[Id] FROM [Book] AS [t0]", result.Statement.CommandText);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void CollectionIncludedOnAProjectedNavigation_IsLoadedFromIt_AndOrdersByTheQueriedKey()
    {
        var result = Rows(Select(Include(Query<Book>(), nameof(Book.Writer), nameof(Writer.Books)), [], ["Writer"]));

        Assert.Equal($"SELECT {Cols<Writer>("t1")} FROM [Book] AS [t0] " + WriterJoin + " ORDER BY [t0].[Id]", result.Statement.CommandText);
        var load = Assert.Single(result.Collections);
        Assert.Equal(0, load.Parent);
        Assert.Equal(nameof(Writer.Books), load.Branch.Navigation.Name);
    }

    [Fact]
    public void RepeatedEntityPath_GetsItsOwnSegmentOverTheSameColumns()
    {
        var result = Rows(Select(Query<Book>(), [], ["Writer", "Writer"]));

        Assert.Equal($"SELECT {Cols<Writer>("t1")} FROM [Book] AS [t0] " + WriterJoin, result.Statement.CommandText);
        Assert.Equal([0, 0], result.Segments.Select(s => s.Offset));
        Assert.Equal([0, 1], result.Roots);
    }

    [Fact]
    public void PagingBeforeSelect_JoinsThePage()
        => Assert.Equal(
            "SELECT [t2].[Name] FROM (SELECT TOP(@p0) [t0].[WriterId], [t0].[Id] FROM [Book] AS [t0] ORDER BY [t0].[Id]) AS [t1] " +
            "INNER JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id] ORDER BY [t1].[Id]",
            Sql(Select(SqlQuery.Take(SqlQuery.OrderBy(Query<Book>(), nameof(Book.Id)), 2), ["Writer.Name"])));

    [Fact]
    public void PagingAfterSelect_AlsoPagesTheEntityRows_AsInEfCore()
        => Assert.Equal(
            Sql(Select(SqlQuery.Take(SqlQuery.OrderBy(Query<Book>(), nameof(Book.Id)), 2), ["Writer.Name"])),
            Sql(SqlProjection.Take(Select(SqlQuery.OrderBy(Query<Book>(), nameof(Book.Id)), ["Writer.Name"]), 2)));

    [Fact]
    public void RowLimit_AppliesToTheJoinedRows()
        => Assert.Equal(
            "SELECT TOP(1) [t1].[Name] FROM [Book] AS [t0] " + WriterJoin,
            Sql(Select(Query<Book>(), ["Writer.Name"]), rowLimit: 1));

    [Fact]
    public void Distinct_IsOverTheJoinedColumns()
        => Assert.Equal(
            "SELECT DISTINCT [t1].[Name] FROM [Book] AS [t0] " + WriterJoin,
            Sql(SqlProjection.Distinct(Select(Query<Book>(), ["Writer.Name"]))));

    [Fact]
    public void DerivedTableOfJoinedColumns_NamesRepeatedColumnsApart()
        => Assert.Equal(
            "SELECT TOP(@p0) [t2].[Id], [t2].[Id0] FROM (SELECT DISTINCT [t0].[Id], [t1].[Id] AS [Id0] FROM [Book] AS [t0] " + WriterJoin + ") AS [t2]",
            Sql(SqlProjection.Take(SqlProjection.Distinct(Select(Query<Book>(), ["Id", "Writer.Id"])), 2)));

    [Fact]
    public void SelectAfterEntityDistinct_ProjectsTheDistinctRows()
        => Assert.Equal(
            $"SELECT [t2].[Name] FROM (SELECT DISTINCT {Cols<Book>("t0")} FROM [Book] AS [t0]) AS [t1] " +
            "INNER JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id]",
            Sql(Select(SqlQuery.Distinct(Query<Book>()), ["Writer.Name"])));

    [Fact]
    public void Count_DoesNotJoinTheProjectionsNavigations_AsInEfCore()
        => Assert.Equal("SELECT COUNT(*) FROM [Book] AS [t0]", Sql(Select(Query<Book>(), ["Writer.Name"]), QueryTerminal.Count));

    [Fact]
    public void Count_OfDistinctJoinedColumns_CountsADerivedTable()
        => Assert.Equal(
            "SELECT COUNT(*) FROM (SELECT DISTINCT [t1].[Name] FROM [Book] AS [t0] " + WriterJoin + ") AS [t2]",
            Sql(SqlProjection.Distinct(Select(Query<Book>(), ["Writer.Name"])), QueryTerminal.Count));

    [Fact]
    public void AggregateSelector_ThroughNavigation_IsJoined()
        => Assert.Equal(
            "SELECT MAX([t1].[Name]) FROM [Book] AS [t0] " + WriterJoin,
            Sql(Query<Book>(), QueryTerminal.Aggregate, aggregate: new SqlAggregate(AggregateFunction.Max, "Writer.Name", AggregateValueType.Column)));

    [Fact]
    public void Aggregate_OfAJoinedColumnProjection_IsJoined()
        => Assert.Equal(
            "SELECT COALESCE(SUM([t1].[Id]), 0) FROM [Book] AS [t0] " + WriterJoin,
            Sql(
                Select(Query<Book>(), ["Writer.Id"]),
                QueryTerminal.Aggregate,
                aggregate: new SqlAggregate(AggregateFunction.Sum, PropertyName: null, AggregateValueType.Int32)));

    [Fact]
    public void Aggregate_OfAnEntityProjection_Throws()
        => Assert.Throws<InvalidOperationException>(() => Sql(
            Select(Query<Book>(), [], ["Writer"]),
            QueryTerminal.Aggregate,
            aggregate: new SqlAggregate(AggregateFunction.Max, PropertyName: null, AggregateValueType.Column)));

    [Fact]
    public void CollectionNavigationInAPath_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => Sql(Select(Query<Writer>(), ["Books.Id"])));
        Assert.Throws<NotSupportedException>(() => Rows(Select(Query<Writer>(), [], ["Books"])));
    }

    [Fact]
    public void UnknownNavigation_Throws()
        => Assert.Throws<InvalidOperationException>(() => Sql(Select(Query<Book>(), ["Missing.Name"])));

    private static IReadOnlyList<ResolvedQueryFilter> NoFilters(BoundEntity entity) => [];

    private static Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> Filters(Type entityType, PredicateTemplate predicate, object value)
        => entity => entity.ClrType == entityType ? [new ResolvedQueryFilter(Key: null, predicate, [value])] : [];

    private static IQueryable<T> Include<T>(IQueryable<T> query, params string[] path)
        where T : class
    {
        var sql = (SqlQueryable<T>)query;
        return sql.WithTail(new IncludeNode(sql.Tail, path));
    }

    private static IQueryable<int> Select<T>(IQueryable<T> source, string[] propertyNames, string[]? entityPaths = null)
        where T : class
        => SqlQuery.Select(source, propertyNames, entityPaths ?? [], static (_, _, _) => 0);

    // Entity and projected queries both carry their chain; tests run under JIT, so reflection is fine here.
    private static QueryNode Tail<T>(IQueryable<T> query)
        => (QueryNode)query.GetType().GetProperty(nameof(ProjectedQueryable<T>.Tail))!.GetValue(query)!;

    private string Sql<T>(
        IQueryable<T> query,
        QueryTerminal terminal = QueryTerminal.Rows,
        int? rowLimit = null,
        SqlAggregate? aggregate = null,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null)
    {
        var tail = Tail(query);
        var entity = BindType(RootType(tail));
        filtersFor ??= NoFilters;
        return SqlBuilder.Build(entity, tail, SqlServerDialect.Instance, terminal, rowLimit, filtersFor(entity), aggregate, filtersFor, BindType).CommandText;
    }

    private IncludeStatement Rows<T>(IQueryable<T> query)
    {
        var tail = Tail(query);
        var entity = BindType(RootType(tail));
        return SqlBuilder.BuildRows(entity, tail, SqlServerDialect.Instance, IncludeTree.FromChain(entity, tail, BindType), NoFilters, bindEntity: BindType);
    }

    private static Type RootType(QueryNode tail)
    {
        var node = tail;
        while (node.Parent is not null)
        {
            node = node.Parent;
        }

        return ((RootNode)node).EntityType;
    }

    private IQueryable<T> Query<T>()
        where T : class
        => SqlQuery.ToSqlQueryable(_db.Set<T>());

    private BoundEntity Bind<T>() => BindType(typeof(T));

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);

    private string Cols<T>(string alias) => string.Join(", ", Bind<T>().Properties.Select(p => $"[{alias}].[{p.ColumnName}]"));
}
