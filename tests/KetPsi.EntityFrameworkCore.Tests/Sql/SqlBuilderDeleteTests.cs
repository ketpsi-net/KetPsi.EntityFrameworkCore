using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary><c>ExecuteDelete</c> statements; the SQL follows the EF Core 10 probe.</summary>
public sealed class SqlBuilderDeleteTests : IDisposable
{
    private const string Items = "[shop].[Items]";
    private static readonly ComparisonTemplate IdEquals = new(nameof(Item.Id), ComparisonOperator.Equal, 0);

    private readonly ItemContext _items = new();
    private readonly LibraryContext _library = new();

    public void Dispose()
    {
        _items.Dispose();
        _library.Dispose();
    }

    [Fact]
    public void Delete_DeletesFromTheQueriedTable()
        => Assert.Equal($"DELETE FROM [t0] FROM {Items} AS [t0]", Delete(Query<Item>(_items)));

    [Fact]
    public void Delete_KeepsPredicatesAndQueryFilters()
    {
        var filter = new ComparisonTemplate(nameof(Item.IsActive), ComparisonOperator.Equal, 0);

        Assert.Equal(
            $"DELETE FROM [t0] FROM {Items} AS [t0] WHERE [t0].[IsActive] = @p0 AND [t0].[Id] = @p1",
            Delete(Where(Query<Item>(_items), IdEquals, 3), entity => [new ResolvedQueryFilter(Key: null, filter, [true])]));
    }

    // An ordering without paging selects the same rows, and distinct entity rows are the rows themselves.
    [Fact]
    public void Delete_IgnoresOrderingAndDistinct()
        => Assert.Equal(
            $"DELETE FROM [t0] FROM {Items} AS [t0]",
            Delete(SqlQuery.Distinct(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Name)))));

    [Fact]
    public void Delete_AfterTake_DeletesTheKeysASubquerySelects()
        => Assert.Equal(
            $"DELETE FROM [t1] FROM {Items} AS [t1] WHERE [t1].[Id] IN (SELECT TOP(@p0) [t0].[Id] FROM {Items} AS [t0] ORDER BY [t0].[Name])",
            Delete(SqlQuery.Take(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Name)), 2)));

    [Fact]
    public void Delete_AfterSkip_KeepsTheOrderingInTheSubquery()
        => Assert.Equal(
            $"DELETE FROM [t1] FROM {Items} AS [t1] WHERE [t1].[Id] IN (SELECT [t0].[Id] FROM {Items} AS [t0] ORDER BY [t0].[Id] OFFSET @p0 ROWS)",
            Delete(SqlQuery.Skip(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Id)), 1)));

    [Fact]
    public void Delete_WhereAfterTake_SelectsTheKeysFromTheDerivedTable()
    {
        var query = Where(SqlQuery.Take(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Id)), 3), IdEquals, 2);

        Assert.Equal(
            $"DELETE FROM [t2] FROM {Items} AS [t2] WHERE [t2].[Id] IN (SELECT [t1].[Id] FROM (SELECT TOP(@p0) " +
            $"[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score] FROM {Items} AS [t0] ORDER BY [t0].[Id]) AS [t1] WHERE [t1].[Id] = @p1)",
            Delete(query));
    }

    [Fact]
    public void Delete_ThroughANavigation_JoinsIt()
        => Assert.Equal(
            "DELETE FROM [t0] FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] WHERE [t1].[Name] = @p0",
            Delete(Where(Query<Book>(_library), new ComparisonTemplate("Writer.Name", ComparisonOperator.Equal, 0), "Ann")));

    // EF Core joins included references, so a required one whose target is filtered out keeps its rows.
    [Fact]
    public void Delete_JoinsIncludedReferencesWithTheirFilters()
    {
        var query = (SqlQueryable<Book>)Query<Book>(_library);
        var included = query.WithTail(new IncludeNode(query.Tail, [nameof(Book.Writer)]));
        var hidden = new ComparisonTemplate(nameof(Writer.Name), ComparisonOperator.NotEqual, 0);

        Assert.Equal(
            "DELETE FROM [t0] FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] AND [t1].[Name] <> @p0",
            Delete(included, entity => entity.ClrType == typeof(Writer) ? [new ResolvedQueryFilter(Key: null, hidden, ["hidden"])] : []));
    }

    [Fact]
    public void Delete_PagedCompositeKey_MatchesEveryKeyPart()
        => Assert.Equal(
            "DELETE FROM [t1] FROM [Section] AS [t1] WHERE EXISTS (SELECT 1 FROM (SELECT TOP(@p0) [t0].[Floor], [t0].[Code] FROM [Section] AS [t0]) " +
            "AS [t2] WHERE [t2].[Floor] = [t1].[Floor] AND [t2].[Code] = [t1].[Code])",
            Delete(SqlQuery.Take(Query<Section>(_library), 1)));

    [Fact]
    public void Delete_PagedKeylessEntity_IsNotSupported()
    {
        using var keyless = new KeylessContext();

        Assert.Equal($"DELETE FROM [t0] FROM [Reports] AS [t0]", Delete(Query<Report>(keyless)));
        Assert.Throws<NotSupportedException>(() => Delete(SqlQuery.Take(Query<Report>(keyless), 1)));
    }

    private static IQueryable<T> Query<T>(DbContext context)
        where T : class
        => SqlQuery.ToSqlQueryable(context.Set<T>());

    private static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, params object?[] values)
        where T : class
        => SqlQuery.Where(source, predicate, values);

    private static string Delete<T>(IQueryable<T> query, Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null)
        where T : class
    {
        var sql = (SqlQueryable<T>)query;
        var model = sql.Context.Model;
        BoundEntity Bind(Type type) => ModelBinder.GetEntity(model, type);
        var entity = Bind(typeof(T));
        return SqlBuilder.BuildDelete(
            entity, sql.Tail, SqlServerDialect.Instance, IncludeTree.FromChain(entity, sql.Tail, Bind), filtersFor ?? (static _ => []), Bind).CommandText;
    }
}
