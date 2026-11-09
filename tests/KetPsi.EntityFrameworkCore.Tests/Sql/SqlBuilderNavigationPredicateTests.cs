using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>Predicates and orderings that read through reference navigations.</summary>
public sealed class SqlBuilderNavigationPredicateTests : IDisposable
{
    private const string WriterJoin = "INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id]";

    private readonly LibraryContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void PropertyThroughRequiredReference_IsInnerJoined()
    {
        var statement = Build(Where(Query<Book>(), new ComparisonTemplate("Writer.Name", ComparisonOperator.Equal, 0), "Ann"));

        Assert.Equal($"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {WriterJoin} WHERE [t1].[Name] = @p0", statement.CommandText);
        Assert.Equal("nvarchar(max)", statement.Parameters[0].Property!.StoreType);
    }

    // A missing optional reference reads null, so != also keeps the rows without one, as in C# null semantics.
    [Fact]
    public void PropertyThroughOptionalReferences_IsLeftJoinedAndNullable()
        => Assert.Equal(
            $"SELECT {Cols<Review>("t0")} FROM [Review] AS [t0] LEFT JOIN [Book] AS [t1] ON [t0].[BookId] = [t1].[Id] " +
            "LEFT JOIN [Writer] AS [t2] ON [t1].[WriterId] = [t2].[Id] WHERE ([t2].[Name] <> @p0 OR [t2].[Name] IS NULL)",
            Build(Where(Query<Review>(), new ComparisonTemplate("Book.Writer.Name", ComparisonOperator.NotEqual, 0), "Ann")).CommandText);

    [Fact]
    public void KeyOfTheTarget_IsReadFromTheForeignKeyWithoutAJoin()
    {
        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] WHERE [t0].[EditorId] = @p0",
            Build(Where(Query<Book>(), new ComparisonTemplate("Editor.Id", ComparisonOperator.Equal, 0), 3)).CommandText);

        // A shadow foreign key too.
        Assert.Equal(
            $"SELECT {Cols<Review>("t0")} FROM [Review] AS [t0] WHERE [t0].[BookId] = @p0",
            Build(Where(Query<Review>(), new ComparisonTemplate("Book.Id", ComparisonOperator.Equal, 0), 3)).CommandText);
    }

    // Only the join applies the target's query filters, so EF Core joins a filtered target even to read its key.
    [Fact]
    public void KeyOrNullCheckOfAFilteredTarget_JoinsTheTarget()
    {
        static IReadOnlyList<ResolvedQueryFilter> EditorFilter(BoundEntity entity)
            => entity.ClrType == typeof(Editor)
                ? [new ResolvedQueryFilter(Key: null, new ComparisonTemplate(nameof(Editor.Id), ComparisonOperator.NotEqual, 0), [7])]
                : [];
        const string EditorJoin = "LEFT JOIN [Editor] AS [t1] ON [t0].[EditorId] = [t1].[Id] AND [t1].[Id] <> @p0";

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {EditorJoin} WHERE [t1].[Id] = @p1",
            Build(Where(Query<Book>(), new ComparisonTemplate("Editor.Id", ComparisonOperator.Equal, 0), 3), EditorFilter).CommandText);
        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {EditorJoin} WHERE [t1].[Id] IS NULL",
            Build(Where(Query<Book>(), new NullCheckTemplate("Editor", isNull: true)), EditorFilter).CommandText);
    }

    [Fact]
    public void NullCheckOfANavigationOnTheDependent_ChecksTheForeignKey()
    {
        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] WHERE [t0].[EditorId] IS NULL",
            Build(Where(Query<Book>(), new NullCheckTemplate("Editor", isNull: true))).CommandText);
        Assert.Equal(
            $"SELECT {Cols<Locker>("t0")} FROM [Locker] AS [t0] WHERE ([t0].[SectionFloor] IS NOT NULL AND [t0].[SectionCode] IS NOT NULL)",
            Build(Where(Query<Locker>(), new NullCheckTemplate("Section", isNull: false))).CommandText);
    }

    [Fact]
    public void NullCheckOfANavigationOnThePrincipal_JoinsTheDependent()
        => Assert.Equal(
            $"SELECT {Cols<Writer>("t0")} FROM [Writer] AS [t0] LEFT JOIN [WriterProfile] AS [t1] ON [t0].[Id] = [t1].[WriterId] " +
            "WHERE [t1].[WriterId] IS NULL",
            Build(Where(Query<Writer>(), new NullCheckTemplate("Profile", isNull: true))).CommandText);

    [Fact]
    public void NegatedNavigationPredicate_PushesTheNegationDown()
        => Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {WriterJoin} WHERE ([t0].[EditorId] IS NOT NULL OR [t1].[Name] <> @p0)",
            Build(Where(
                Query<Book>(),
                new NotTemplate(new AndTemplate(new NullCheckTemplate("Editor", isNull: true), new ComparisonTemplate("Writer.Name", ComparisonOperator.Equal, 0))),
                "Ann")).CommandText);

    [Fact]
    public void PredicatesAndOrderingsThroughTheSameNavigation_ShareOneJoin()
    {
        var query = SqlQuery.ThenBy(
            SqlQuery.OrderByDescending(Where(Query<Book>(), new ComparisonTemplate("Writer.Name", ComparisonOperator.NotEqual, 0), "Ann"), "Writer.Name"),
            nameof(Book.Id));

        Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {WriterJoin} WHERE [t1].[Name] <> @p0 ORDER BY [t1].[Name] DESC, [t0].[Id]",
            Build(query).CommandText);
    }

    [Fact]
    public void OrderingThroughANavigation_IsKeptByADerivedTable()
    {
        var query = Where(SqlQuery.Take(SqlQuery.OrderBy(Query<Book>(), "Writer.Name"), 5), new ComparisonTemplate("Writer.Id", ComparisonOperator.Equal, 0), 1);

        Assert.Equal(
            $"SELECT {Cols<Book>("t2")} FROM (SELECT TOP(@p0) {Cols<Book>("t0")}, [t1].[Name] FROM [Book] AS [t0] {WriterJoin} ORDER BY [t1].[Name]) " +
            "AS [t2] WHERE [t2].[WriterId] = @p1 ORDER BY [t2].[Name]",
            Build(query).CommandText);
    }

    [Fact]
    public void PredicateThroughANavigation_KeepsTheJoinedEntitysFilters()
        => Assert.Equal(
            $"SELECT {Cols<Book>("t0")} FROM [Book] AS [t0] {WriterJoin} AND [t1].[Name] <> @p0 WHERE [t1].[Name] > @p1",
            Build(
                Where(Query<Book>(), new ComparisonTemplate("Writer.Name", ComparisonOperator.GreaterThan, 0), "A"),
                entity => entity.ClrType == typeof(Writer)
                    ? [new ResolvedQueryFilter(Key: null, new ComparisonTemplate(nameof(Writer.Name), ComparisonOperator.NotEqual, 0), ["hidden"])]
                    : []).CommandText);

    [Fact]
    public void UnknownNavigationOrCollection_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(Where(Query<Book>(), new ComparisonTemplate("Missing.Name", ComparisonOperator.Equal, 0), "x")));
        Assert.Throws<NotSupportedException>(() => Build(Where(Query<Writer>(), new ComparisonTemplate("Books.Id", ComparisonOperator.Equal, 0), 1)));
    }

    private static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, params object?[] values)
        where T : class
        => SqlQuery.Where(source, predicate, values);

    private SqlStatement Build<T>(IQueryable<T> query, Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null)
        where T : class
    {
        var entity = BindType(typeof(T));
        filtersFor ??= static _ => [];
        return SqlBuilder.Build(
            entity, ((SqlQueryable<T>)query).Tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: null, filtersFor(entity),
            aggregate: null, filtersFor, BindType);
    }

    private IQueryable<T> Query<T>()
        where T : class
        => SqlQuery.ToSqlQueryable(_db.Set<T>());

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);

    private string Cols<T>(string alias) => string.Join(", ", BindType(typeof(T)).Properties.Select(p => $"[{alias}].[{p.ColumnName}]"));
}
