using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderIncludeTests : IDisposable
{
    private static readonly ComparisonTemplate NameIsNot = new(nameof(Writer.Name), ComparisonOperator.NotEqual, 0);
    private static readonly ComparisonTemplate IdAbove = new(nameof(Book.Id), ComparisonOperator.GreaterThan, 0);

    private readonly LibraryContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void RequiredReference_IsInnerJoined()
    {
        var result = Rows<Book>(q => Include(q, nameof(Book.Writer)));

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id]",
            result.Statement.CommandText);
        Assert.Equal(
            [
                new EntitySegment(Bind<Book>(), 0, null, null),
                new EntitySegment(Bind<Writer>(), Bind<Book>().Properties.Count, 0, Bind<Book>().GetNavigation(nameof(Book.Writer))),
            ],
            result.Segments);
        Assert.Empty(result.Collections);
    }

    [Fact]
    public void OptionalReference_IsLeftJoined()
        => Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Editor>("t1")} FROM [Book] AS [t0] LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id]",
            Rows<Book>(q => Include(q, nameof(Book.Editor))).Statement.CommandText);

    [Fact]
    public void ThenInclude_JoinsFromTheIncludedEntity()
    {
        var result = Rows<Book>(q => Include(q, nameof(Book.Writer), nameof(Writer.Profile)));

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Writer>("t1")}, {Cols<WriterProfile>("t2")} FROM [Book] AS [t0] " +
            "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] " +
            "LEFT JOIN [WriterProfile] AS [t2] ON [t1].[Id] = [t2].[WriterId]",
            result.Statement.CommandText);
        Assert.Equal(1, result.Segments[2].Parent);
        Assert.Equal(Bind<Book>().Properties.Count + Bind<Writer>().Properties.Count, result.Segments[2].Offset);
    }

    [Fact]
    public void RequiredReference_AfterOptionalOne_IsLeftJoined()
        => Assert.Equal(
            $"SELECT {Cols<Review>("t0")}, {Cols<Book>("t1")}, {Cols<Writer>("t2")} FROM [Review] AS [t0] " +
            "LEFT JOIN [Book] AS [t1] ON [t0].[BookId] = [t1].[Id] " +
            "LEFT JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id]",
            Rows<Review>(q => Include(q, nameof(Review.Book), nameof(Book.Writer))).Statement.CommandText);

    [Fact]
    public void CompositeForeignKey_JoinsEveryColumn()
        => Assert.Equal(
            $"SELECT {Cols<Locker>("t0")}, {Cols<Section>("t1")} FROM [Locker] AS [t0] " +
            "INNER JOIN [Section] AS [t1] ON [t0].[SectionFloor] = [t1].[Floor] AND [t0].[SectionCode] = [t1].[Code]",
            Rows<Locker>(q => Include(q, nameof(Locker.Section))).Statement.CommandText);

    [Fact]
    public void JoinedEntityFilters_AreInTheJoinCondition_AfterRootFilters()
    {
        var result = Rows<Book>(
            q => Include(SqlQuery.Where(q, IdAbove, [5]), nameof(Book.Writer)),
            Filters(typeof(Writer), NameIsNot, "hidden"));

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] " +
            "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] AND [t1].[Name] <> @p1 WHERE [t0].[Id] > @p0",
            result.Statement.CommandText);
        Assert.Equal([5, "hidden"], result.Statement.Parameters.Select(p => p.Value));
        Assert.Same(Bind<Writer>().GetProperty(nameof(Writer.Name)), result.Statement.Parameters[1].Property);
    }

    [Fact]
    public void RootFilters_StayInWhere()
        => Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] " +
            "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] WHERE [t0].[Id] > @p0",
            Rows<Book>(q => Include(q, nameof(Book.Writer)), Filters(typeof(Book), IdAbove, 0)).Statement.CommandText);

    [Fact]
    public void PagedQuery_JoinsReferencesOutsideThePage()
        => Assert.Equal(
            $"SELECT {Cols<Book>("t1")}, {Cols<Writer>("t2")} FROM (" +
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] ORDER BY [t0].[Id] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY) AS [t1] " +
            "INNER JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id] ORDER BY [t1].[Id]",
            Rows<Book>(q => Include(SqlQuery.Take(SqlQuery.Skip(SqlQuery.OrderBy(q, nameof(Book.Id)), 1), 2), nameof(Book.Writer)))
                .Statement.CommandText);

    [Fact]
    public void RowLimit_AppliesToTheJoinedRows()
        => Assert.Equal(
            $"SELECT TOP(1) {Cols<Book>("t0")}, {Cols<Writer>("t1")} FROM [Book] AS [t0] " +
            "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] ORDER BY [t0].[Id]",
            Rows<Book>(q => Include(SqlQuery.OrderBy(q, nameof(Book.Id)), nameof(Book.Writer)), rowLimit: 1).Statement.CommandText);

    [Fact]
    public void Collection_IsNotJoined_AndOrdersByKey()
    {
        var result = Rows<Writer>(q => Include(q, nameof(Writer.Books)));

        Assert.Equal($"SELECT {Cols<Writer>("t0")} FROM [Writer] AS [t0] ORDER BY [t0].[Id]", result.Statement.CommandText);
        var load = Assert.Single(result.Collections);
        Assert.Equal(0, load.Parent);
        Assert.Equal(nameof(Writer.Books), load.Branch.Navigation.Name);
        Assert.Single(result.Segments);
    }

    [Fact]
    public void Collection_AppendsKeyAfterUserOrdering()
        => Assert.Equal(
            $"SELECT {Cols<Writer>("t0")} FROM [Writer] AS [t0] ORDER BY [t0].[Name] DESC, [t0].[Id]",
            Rows<Writer>(q => Include(SqlQuery.OrderByDescending(q, nameof(Writer.Name)), nameof(Writer.Books))).Statement.CommandText);

    [Fact]
    public void Collection_KeyAlreadyOrdered_IsNotRepeated()
        => Assert.Equal(
            $"SELECT {Cols<Writer>("t0")} FROM [Writer] AS [t0] ORDER BY [t0].[Id] DESC",
            Rows<Writer>(q => Include(SqlQuery.OrderByDescending(q, nameof(Writer.Id)), nameof(Writer.Books))).Statement.CommandText);

    [Fact]
    public void Collection_KeyOrderingIsPartOfThePage()
        => Assert.Equal(
            $"SELECT TOP(@p0) {Cols<Writer>("t0")} FROM [Writer] AS [t0] ORDER BY [t0].[Name], [t0].[Id]",
            Rows<Writer>(q => Include(SqlQuery.Take(SqlQuery.OrderBy(q, nameof(Writer.Name)), 2), nameof(Writer.Books))).Statement.CommandText);

    [Fact]
    public void ReferenceOnly_DoesNotAddKeyOrdering()
        => Assert.DoesNotContain("ORDER BY", Rows<Book>(q => Include(q, nameof(Book.Writer))).Statement.CommandText, StringComparison.Ordinal);

    [Fact]
    public void CollectionUnderReference_IsLoadedFromTheReferenceSegment()
    {
        var result = Rows<Book>(q => Include(q, nameof(Book.Writer), nameof(Writer.Books)));

        var load = Assert.Single(result.Collections);
        Assert.Equal(1, load.Parent);
        Assert.Equal(nameof(Writer.Books), load.Branch.Navigation.Name);
        Assert.DoesNotContain("ORDER BY", result.Statement.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedPaths_AreMergedInFirstSeenOrder()
    {
        var result = Rows<Book>(q =>
            Include(Include(Include(q, nameof(Book.Writer)), nameof(Book.Editor)), nameof(Book.Writer), nameof(Writer.Profile)));

        Assert.Equal(
            [null, nameof(Book.Writer), nameof(Writer.Profile), nameof(Book.Editor)],
            result.Segments.Select(s => s.Navigation?.Name));
        Assert.Equal(3, result.Statement.CommandText.Split(" JOIN ").Length - 1);
    }

    [Fact]
    public void ScalarTerminals_IgnoreIncludes()
    {
        var entity = Bind<Book>();
        var query = Include(Query<Book>(), nameof(Book.Writer));

        var count = SqlBuilder.Build(entity, Tail(query), SqlServerDialect.Instance, QueryTerminal.Count);

        Assert.Equal("SELECT COUNT(*) FROM [Book] AS [t0]", count.CommandText);
    }

    [Fact]
    public void BuildCollection_MatchesParentKeysAndOrdersByKey()
    {
        var branch = Branch<Writer>(nameof(Writer.Books));

        var result = SqlBuilder.BuildCollection(branch, [[1], [2]], SqlServerDialect.Instance, NoFilters);

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] " +
            "WHERE [t0].[WriterId] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$')) ORDER BY [t0].[Id]",
            result.Statement.CommandText);
        var parameter = Assert.Single(result.Statement.Parameters);
        Assert.Equal("[1,2]", parameter.Value);
        Assert.Null(parameter.Property);
        Assert.Equal(Bind<Book>(), Assert.Single(result.Segments).Entity);
    }

    [Fact]
    public void BuildCollection_AppliesChildFiltersAndJoinsChildReferences()
    {
        var branch = Branch<Writer>(nameof(Writer.Books), nameof(Book.Editor));

        var result = SqlBuilder.BuildCollection(branch, [[1]], SqlServerDialect.Instance, Filters(typeof(Book), IdAbove, 3));

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")}, {Cols<Editor>("t1")} FROM [Book] AS [t0] LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id] " +
            "WHERE [t0].[Id] > @p0 AND [t0].[WriterId] IN (SELECT [value] FROM OPENJSON(@p1) WITH ([value] int '$')) ORDER BY [t0].[Id]",
            result.Statement.CommandText);
    }

    [Fact]
    public void BuildCollection_ListsNestedCollections()
    {
        var branch = Branch<Writer>(nameof(Writer.Books), nameof(Book.Reviews));

        var result = SqlBuilder.BuildCollection(branch, [[1]], SqlServerDialect.Instance, NoFilters);

        var load = Assert.Single(result.Collections);
        Assert.Equal(0, load.Parent);
        Assert.Equal(nameof(Book.Reviews), load.Branch.Navigation.Name);
    }

    [Fact]
    public void BuildCollection_CompositeKey_MatchesTuples()
    {
        var branch = Branch<Section>(nameof(Section.Lockers));

        var result = SqlBuilder.BuildCollection(branch, [[1, "A"]], SqlServerDialect.Instance, NoFilters);

        Assert.Equal(
            $"SELECT {Cols<Locker>("t0")} FROM [Locker] AS [t0] " +
            "WHERE EXISTS (SELECT 1 FROM OPENJSON(@p0) WITH ([v0] int '$[0]', [v1] nvarchar(450) '$[1]') AS [k] " +
            "WHERE [k].[v0] = [t0].[SectionFloor] AND [k].[v1] = [t0].[SectionCode]) ORDER BY [t0].[Id]",
            result.Statement.CommandText);
        Assert.Equal("[[1,\"A\"]]", Assert.Single(result.Statement.Parameters).Value);
    }

    [Fact]
    public void BuildCollection_NoKeys_Throws()
        => Assert.Throws<ArgumentException>(
            () => SqlBuilder.BuildCollection(Branch<Writer>(nameof(Writer.Books)), [], SqlServerDialect.Instance, NoFilters));

    [Fact]
    public void BuildCollection_Reference_Throws()
        => Assert.Throws<ArgumentException>(
            () => SqlBuilder.BuildCollection(Branch<Book>(nameof(Book.Writer)), [[1]], SqlServerDialect.Instance, NoFilters));

    [Fact]
    public void IncludeTree_WithoutIncludes_IsNull()
        => Assert.Null(IncludeTree.FromChain(Bind<Book>(), Tail(Query<Book>()), BindType));

    [Fact]
    public void IncludeTree_UnknownNavigation_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => IncludeTree.FromChain(Bind<Book>(), Tail(Include(Query<Book>(), "Missing")), BindType));

    [Fact]
    public void IncludeTree_ManyToMany_Throws()
        => Assert.Throws<NotSupportedException>(
            () => IncludeTree.FromChain(Bind<Book>(), Tail(Include(Query<Book>(), nameof(Book.Tags))), BindType));

    private static IReadOnlyList<ResolvedQueryFilter> NoFilters(BoundEntity entity) => [];

    private static Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> Filters(Type entityType, PredicateTemplate predicate, object value)
        => entity => entity.ClrType == entityType ? [new ResolvedQueryFilter(Key: null, predicate, [value])] : [];

    private static IQueryable<T> Include<T>(IQueryable<T> query, params string[] path)
        where T : class
    {
        var sql = (SqlQueryable<T>)query;
        return sql.WithTail(new IncludeNode(sql.Tail, path));
    }

    private static QueryNode Tail<T>(IQueryable<T> query)
        where T : class
        => ((SqlQueryable<T>)query).Tail;

    private IQueryable<T> Query<T>()
        where T : class
        => SqlQuery.ToSqlQueryable(_db.Set<T>());

    private BoundEntity Bind<T>() => BindType(typeof(T));

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);

    private string Cols<T>(string alias) => string.Join(", ", Bind<T>().Properties.Select(p => $"[{alias}].[{p.ColumnName}]"));

    private IncludeStatement Rows<T>(
        Func<IQueryable<T>, IQueryable<T>> compose,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null,
        int? rowLimit = null)
        where T : class
    {
        var entity = Bind<T>();
        var tail = Tail(compose(Query<T>()));
        var includes = IncludeTree.FromChain(entity, tail, BindType)!;
        return SqlBuilder.BuildRows(entity, tail, SqlServerDialect.Instance, includes, filtersFor ?? NoFilters, rowLimit);
    }

    private IncludeBranch Branch<T>(params string[] path)
        where T : class
        => IncludeTree.FromChain(Bind<T>(), Tail(Include(Query<T>(), path)), BindType)!.Branches[0];
}
