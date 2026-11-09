using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary><c>Join</c> and the operators that read through its result; the SQL follows the EF Core 10 probe.</summary>
public sealed class SqlBuilderJoinTests : IDisposable
{
    private const string WriterJoin = "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id]";

    private readonly LibraryContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Join_InnerJoinsOnTheKeys()
        => Assert.Equal(
            $"SELECT [t0].[Id], [t1].[Name] FROM [Book] AS [t0] {WriterJoin}",
            Sql(Join(Query<Book>(), Query<Writer>(), ["WriterId"], ["Id"], ["Id", "$0.Name"])));

    [Fact]
    public void Join_PutsTheInnerFiltersInOnAndTheInnerPredicatesInWhere()
    {
        var inner = Where(Query<Writer>(), new ComparisonTemplate(nameof(Writer.Name), ComparisonOperator.NotEqual, 0), "Ann");
        var hidden = new ComparisonTemplate(nameof(Writer.Name), ComparisonOperator.NotEqual, 0);

        Assert.Equal(
            $"SELECT [t0].[Id] FROM [Book] AS [t0] {WriterJoin} AND [t1].[Name] <> @p0 WHERE [t1].[Name] <> @p1",
            Sql(
                Join(Query<Book>(), inner, ["WriterId"], ["Id"], ["Id"]),
                entity => entity.ClrType == typeof(Writer) ? [new ResolvedQueryFilter(Key: null, hidden, ["hidden"])] : []));
    }

    [Fact]
    public void Join_InnerPredicateThroughANavigation_JoinsItAfterTheInnerTable()
        => Assert.Equal(
            $"SELECT [t0].[Id] FROM [Book] AS [t0] {WriterJoin} LEFT JOIN [WriterProfile] AS [t2] ON [t1].[Id] = [t2].[WriterId] " +
            "WHERE [t2].[WriterId] IS NOT NULL",
            Sql(Join(Query<Book>(), Where(Query<Writer>(), new NullCheckTemplate(nameof(Writer.Profile), isNull: false)), ["WriterId"], ["Id"], ["Id"])));

    [Fact]
    public void Join_AfterPaging_JoinsTheDerivedTable()
        => Assert.Equal(
            $"SELECT [t1].[Id] FROM (SELECT TOP(@p0) {Cols<Book>("t0")} FROM [Book] AS [t0] ORDER BY [t0].[Id]) AS [t1] " +
            "INNER JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id] ORDER BY [t1].[Id]",
            Sql(Join(SqlQuery.Take(SqlQuery.OrderBy(Query<Book>(), nameof(Book.Id)), 2), Query<Writer>(), ["WriterId"], ["Id"], ["Id"])));

    [Fact]
    public void Join_CompositeKeys_MatchEveryPart()
        => Assert.Equal(
            "SELECT [t0].[Id] FROM [Locker] AS [t0] INNER JOIN [Section] AS [t1] ON [t0].[SectionFloor] = [t1].[Floor] AND [t0].[SectionCode] = [t1].[Code]",
            Sql(Join(Query<Locker>(), Query<Section>(), ["SectionFloor", "SectionCode"], ["Floor", "Code"], ["Id"])));

    [Fact]
    public void Join_OfEntities_ReadsBoth()
    {
        var result = Rows(Join(Query<Book>(), Query<Writer>(), ["WriterId"], ["Id"], [], ["", "$0"]));

        Assert.Equal($"SELECT {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] {WriterJoin}", result.Statement.CommandText);
        Assert.Equal([Bind<Book>(), Bind<Writer>()], result.Segments.Select(static s => s.Entity));
    }

    // Query syntax: Join(..., (b, w) => new { b, w }).Where(x => x.w.Name != name).OrderBy(x => x.b.Id).Select(x => ...).
    [Fact]
    public void OperatorsThroughTheJoinedPair_StayInOneSelect()
    {
        var pair = Join(Query<Book>(), Query<Writer>(), ["WriterId"], ["Id"], [], ["", "$0"]);
        var filtered = SqlProjection.Where(pair, new ComparisonTemplate("$0.Name", ComparisonOperator.NotEqual, 0), ["Ann"]);
        var ordered = SqlProjection.ThenByDescending(SqlProjection.OrderBy(filtered, "$0.Name"), "Id");
        var selected = SqlProjection.Select(ordered, ["Id", "$0.Name"], [], static (_, _, _) => 0);

        Assert.Equal(
            $"SELECT [t0].[Id], [t1].[Name] FROM [Book] AS [t0] {WriterJoin} WHERE [t1].[Name] <> @p0 ORDER BY [t1].[Name], [t0].[Id] DESC",
            Sql(selected));
    }

    [Fact]
    public void SecondJoin_ReadsTheFirstJoinsEntity()
    {
        var pair = Join(Query<Book>(), Query<Writer>(), ["WriterId"], ["Id"], [], ["", "$0"]);
        var second = SqlProjection.Join(pair, Query<WriterProfile>(), ["$0.Id"], ["WriterId"], ["Id", "$1.Id"], [], static (_, _, _) => 0);

        Assert.Equal(
            $"SELECT [t0].[Id], [t2].[Id] FROM [Book] AS [t0] {WriterJoin} INNER JOIN [WriterProfile] AS [t2] ON [t1].[Id] = [t2].[WriterId]",
            Sql(second));
    }

    [Fact]
    public void Count_AfterJoin_KeepsTheJoin()
        => Assert.Equal(
            $"SELECT COUNT(*) FROM [Book] AS [t0] {WriterJoin}",
            Sql(Join(Query<Book>(), Query<Writer>(), ["WriterId"], ["Id"], ["$0.Name"]), terminal: QueryTerminal.Count));

    [Fact]
    public void PagingTwiceAfterJoin_SelectsTheJoinedColumnsFirst()
    {
        var joined = Join(SqlQuery.OrderBy(Query<Book>(), nameof(Book.Id)), Query<Writer>(), ["WriterId"], ["Id"], ["$0.Name"]);

        Assert.Equal(
            $"SELECT [t2].[Name] FROM (SELECT TOP(@p0) [t1].[Name], [t0].[Id] FROM [Book] AS [t0] {WriterJoin} ORDER BY [t0].[Id]) AS [t2] " +
            "ORDER BY [t2].[Id] OFFSET @p1 ROWS",
            Sql(SqlProjection.Skip(SqlProjection.Take(joined, 3), 1)));
    }

    [Fact]
    public void Join_OfAnotherContext_Throws()
    {
        using var other = new LibraryContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => Join(Query<Book>(), SqlQuery.ToSqlQueryable(other.Set<Writer>()), ["WriterId"], ["Id"], ["Id"]));

        Assert.Equal(
            "Cannot use multiple context instances within a single query execution. Ensure the query uses a single context instance.",
            exception.Message);
    }

    [Fact]
    public void IgnoreQueryFilters_InTheInnerQuery_AppliesToTheWholeQuery()
    {
        var inner = SqlQuery.IgnoreQueryFilters(Query<Writer>());
        var joined = (ProjectedQueryable<int>)Join(Query<Book>(), inner, ["WriterId"], ["Id"], ["Id"]);

        Assert.True(IgnoredQueryFilters.FromChain(joined.Tail).All);
    }

    private static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, params object?[] values)
        where T : class
        => SqlQuery.Where(source, predicate, values);

    private static IQueryable<int> Join<TOuter, TInner>(
        IQueryable<TOuter> outer, IQueryable<TInner> inner, string[] outerKeys, string[] innerKeys, string[] propertyNames, string[]? entityPaths = null)
        where TOuter : class
        where TInner : class
        => SqlQuery.Join(outer, inner, outerKeys, innerKeys, propertyNames, entityPaths ?? [], static (_, _, _) => 0);

    private string Sql<T>(
        IQueryable<T> query, Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null, QueryTerminal terminal = QueryTerminal.Rows)
    {
        var tail = ((ProjectedQueryable<T>)query).Tail;
        var entity = BindType(QueryRootType(tail));
        filtersFor ??= static _ => [];
        return SqlBuilder.Build(entity, tail, SqlServerDialect.Instance, terminal, rowLimit: null, filtersFor(entity), aggregate: null, filtersFor, BindType)
            .CommandText;
    }

    private IncludeStatement Rows<T>(IQueryable<T> query)
    {
        var tail = ((ProjectedQueryable<T>)query).Tail;
        var entity = BindType(QueryRootType(tail));
        return SqlBuilder.BuildRows(entity, tail, SqlServerDialect.Instance, includes: null, static _ => [], bindEntity: BindType);
    }

    private static Type QueryRootType(QueryNode tail)
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
