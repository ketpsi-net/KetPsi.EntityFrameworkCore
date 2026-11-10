using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.Data.SqlClient;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>Queries from <c>FromSql</c>; the SQL follows the EF Core 8, 9 and 10 probe.</summary>
public sealed class SqlBuilderRawSqlTests : IDisposable
{
    private const string Columns = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score]";
    private static readonly ComparisonTemplate IdEquals = new(nameof(Item.Id), ComparisonOperator.Equal, 0);

    private readonly ItemContext _items = new();
    private readonly LibraryContext _library = new();

    public void Dispose()
    {
        _items.Dispose();
        _library.Dispose();
    }

    [Fact]
    public void Uncomposed_RunsAsWrittenAndReadsColumnsByName()
    {
        var statement = Build(SqlQuery.FromSqlRaw(_items.Items, "EXEC GetItems", null));

        Assert.Equal("EXEC GetItems", statement.CommandText);
        Assert.Equal(["Id", "IsActive", "Name", "Score"], statement.ColumnsByName);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public void Placeholders_BecomeParametersInOrder()
    {
        var minimum = 3;
        var statement = Build(SqlQuery.FromSql(_items.Items, $"SELECT * FROM Items WHERE Score >= {minimum} AND Name <> {"x"} AND {(string?)null} IS NULL"));

        Assert.Equal("SELECT * FROM Items WHERE Score >= @p0 AND Name <> @p1 AND @p2 IS NULL", statement.CommandText);
        Assert.Equal([3, "x", DBNull.Value], statement.Parameters.Select(static p => p.Value));
    }

    [Fact]
    public void Composed_WrapsTheSqlInADerivedTableAfterItsParameters()
    {
        var statement = Build(Where(SqlQuery.FromSql(_items.Items, $"SELECT * FROM Items WHERE Score >= {3}"), IdEquals, 7));

        Assert.Equal($"SELECT {Columns} FROM (\nSELECT * FROM Items WHERE Score >= @p0\n) AS [t0] WHERE [t0].[Id] = @p1", statement.CommandText);
        Assert.Null(statement.ColumnsByName);
        Assert.Equal([3, 7], statement.Parameters.Select(static p => p.Value));
    }

    // A line comment at the end must not swallow the closing parenthesis.
    [Fact]
    public void Composed_KeepsATrailingLineComment()
        => Assert.Equal(
            $"SELECT COUNT(*) FROM (\nSELECT * FROM Items -- all\n) AS [t0]",
            Build(SqlQuery.FromSqlRaw(_items.Items, "SELECT * FROM Items -- all", null), QueryTerminal.Count).CommandText);

    [Fact]
    public void QueryFilters_ComposeOverTheSql()
    {
        var filter = new ComparisonTemplate(nameof(Item.IsActive), ComparisonOperator.Equal, 0);

        var statement = Build(SqlQuery.FromSqlRaw(_items.Items, "SELECT * FROM Items", null), filters: [new ResolvedQueryFilter(Key: null, filter, [true])]);

        Assert.Equal($"SELECT {Columns} FROM (\nSELECT * FROM Items\n) AS [t0] WHERE [t0].[IsActive] = @p0", statement.CommandText);
    }

    [Fact]
    public void TrackingOperators_DoNotCompose()
        => Assert.Equal("EXEC GetItems", Build(SqlQuery.IgnoreQueryFilters(SqlQuery.AsNoTracking(SqlQuery.FromSqlRaw(_items.Items, "EXEC GetItems", null)))).CommandText);

    [Fact]
    public void RowLimit_Composes()
        => Assert.StartsWith("SELECT TOP(1) ", Build(SqlQuery.FromSqlRaw(_items.Items, "SELECT * FROM Items", null), rowLimit: 1).CommandText, StringComparison.Ordinal);

    [Theory]
    [InlineData("EXEC GetItems")]
    [InlineData("WITH x AS (SELECT * FROM Items) SELECT * FROM x")]
    [InlineData("(SELECT * FROM Items)")]
    public void ComposingOverNonComposableSql_ThrowsLikeEfCore(string sql)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(SqlQuery.Take(SqlQuery.FromSqlRaw(_items.Items, sql, null), 2)));

        Assert.StartsWith("'FromSql' or 'SqlQuery' was called with non-composable SQL and with a query composing over it.", exception.Message, StringComparison.Ordinal);
    }

    // The statement's own parameters skip the names of the DbParameters the SQL was given, as EF Core's constants do.
    [Fact]
    public void GivenDbParameters_ArePassedAsTheyAreAndTheirNamesSkipped()
    {
        var given = new SqlParameter("@p0", 3);
        var statement = Build(Where(SqlQuery.FromSqlRaw(_items.Items, "SELECT * FROM Items WHERE Score >= @p0 AND Id <> {0}", [given]), IdEquals, 7));

        Assert.Equal($"SELECT {Columns} FROM (\nSELECT * FROM Items WHERE Score >= @p0 AND Id <> @p0\n) AS [t0] WHERE [t0].[Id] = @p1", statement.CommandText);
        Assert.Same(given, statement.Parameters[0].Value);
        Assert.Equal("@p1", statement.Parameters[1].Name);
    }

    [Fact]
    public void FromSqlRaw_WithoutArguments_KeepsBraces_WhileFromSqlFormats()
    {
        Assert.Equal("SELECT '{x}' AS Name", Build(SqlQuery.FromSqlRaw(_items.Items, "SELECT '{x}' AS Name", null)).CommandText);
        Assert.Equal("SELECT '{x}' AS Name", Build(SqlQuery.FromSql(_items.Items, $"SELECT '{{x}}' AS Name")).CommandText);
    }

    [Fact]
    public void ExecuteDelete_DeletesTheKeysTheSqlSelects()
        => Assert.Equal(
            "DELETE FROM [t1] FROM [shop].[Items] AS [t1] WHERE [t1].[Id] IN (SELECT [t0].[Id] FROM (\nSELECT * FROM Items WHERE Id = @p0\n) AS [t0])",
            Delete(SqlQuery.FromSql(_items.Items, $"SELECT * FROM Items WHERE Id = {10}")));

    [Fact]
    public void Join_OfSqlOfFromSql_JoinsItsDerivedTable()
    {
        var joined = SqlQuery.Join(
            SqlQuery.ToSqlQueryable(_library.Set<Book>()),
            SqlQuery.FromSql(_library.Set<Writer>(), $"SELECT * FROM Writer WHERE Name <> {"x"}"),
            ["WriterId"],
            ["Id"],
            ["Id"],
            [],
            static (_, _, _) => 0);
        var tail = ((ProjectedQueryable<int>)joined).Tail;
        BoundEntity Bind(Type type) => ModelBinder.GetEntity(_library.Model, type);

        var statement = SqlBuilder.Build(Bind(typeof(Book)), tail, SqlServerDialect.Instance, QueryTerminal.Rows, filters: [], filtersFor: static _ => [], bindEntity: Bind);

        Assert.Equal(
            "SELECT [t0].[Id] FROM [Book] AS [t0] INNER JOIN (\nSELECT * FROM Writer WHERE Name <> @p0\n) AS [t1] ON [t0].[WriterId] = [t1].[Id]",
            statement.CommandText);
    }

    [Theory]
    [InlineData("SELECT * FROM Items", true)]
    [InlineData("select * from Items", true)]
    [InlineData("\n \t SELECT 1", true)]
    [InlineData("-- note\nSELECT 1", true)]
    [InlineData("/* note */ SELECT 1", true)]
    [InlineData("SELECT", true)]
    [InlineData("EXEC GetItems", false)]
    [InlineData("WITH x AS (SELECT 1 AS Id) SELECT * FROM x", false)]
    [InlineData("(SELECT 1)", false)]
    [InlineData("SELECTED", false)]
    [InlineData("-- only a comment", false)]
    [InlineData("/* unclosed SELECT 1", false)]
    public void IsComposable_FollowsEfCore(string sql, bool composable)
        => Assert.Equal(composable, RawSql.IsComposable(sql));

    private static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, params object?[] values)
        where T : class
        => SqlQuery.Where(source, predicate, values);

    private SqlStatement Build(
        IQueryable<Item> query, QueryTerminal terminal = QueryTerminal.Rows, int? rowLimit = null, IReadOnlyList<ResolvedQueryFilter>? filters = null)
        => SqlBuilder.Build(ModelBinder.GetEntity(_items.Model, typeof(Item)), ((SqlQueryable<Item>)query).Tail, SqlServerDialect.Instance, terminal, rowLimit, filters);

    private string Delete(IQueryable<Item> query)
        => SqlBuilder.BuildDelete(
            ModelBinder.GetEntity(_items.Model, typeof(Item)), ((SqlQueryable<Item>)query).Tail, SqlServerDialect.Instance, includes: null, static _ => []).CommandText;
}
