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

/// <summary><c>ExecuteUpdate</c> statements; the SQL follows the EF Core 10 probe.</summary>
public sealed class SqlBuilderUpdateTests : IDisposable
{
    private const string Items = "[shop].[Items]";
    private static readonly string NoCountOff = SqlBuilderDeleteTests.NoCountOff;
    private static readonly ComparisonTemplate IdEquals = new(nameof(Item.Id), ComparisonOperator.Equal, 0);

    private readonly ItemContext _items = new();
    private readonly LibraryContext _library = new();
    private readonly GadgetContext _gadgets = new();

    public void Dispose()
    {
        _items.Dispose();
        _library.Dispose();
        _gadgets.Dispose();
    }

    [Fact]
    public void Update_SetsAValueOnTheQueriedTable()
    {
        var statement = Update(Query<Item>(_items), Value(nameof(Item.Name), "x"));

        Assert.Equal($"{NoCountOff}UPDATE [t0] SET [t0].[Name] = @p0 FROM {Items} AS [t0]", statement.CommandText);
        Assert.Equal(nameof(Item.Name), Assert.Single(statement.Parameters).Property!.Name);
    }

    [Fact]
    public void Update_KeepsPredicatesAndQueryFilters_AndSetsEachPropertyInOrder()
    {
        var filter = new ComparisonTemplate(nameof(Item.IsActive), ComparisonOperator.Equal, 0);
        var statement = Update(
            Where(Query<Item>(_items), IdEquals, 3),
            entity => [new ResolvedQueryFilter(Key: null, filter, [true])],
            Set(nameof(Item.Score), new BinaryValueTemplate(ValueOperator.Add, P(nameof(Item.Score)), S(0)), 1),
            Set(nameof(Item.Name), P(nameof(Item.Name))));

        Assert.Equal(
            $"{NoCountOff}UPDATE [t0] SET [t0].[Score] = [t0].[Score] + @p2, [t0].[Name] = [t0].[Name] FROM {Items} AS [t0] WHERE [t0].[IsActive] = @p0 AND [t0].[Id] = @p1",
            statement.CommandText);
    }

    [Fact]
    public void Update_ToNull_SetsNull()
        => Assert.Equal(
            $"{NoCountOff}UPDATE [t0] SET [t0].[Score] = NULL FROM {Items} AS [t0]",
            Update(Query<Item>(_items), Value(nameof(Item.Score), null)).CommandText);

    [Fact]
    public void Update_ConvertsTheValueLikeTheColumn()
    {
        var statement = Update(Query<Gadget>(_gadgets), Value(nameof(Gadget.Grade), GadgetGrade.B));

        Assert.Equal($"{NoCountOff}UPDATE [t0] SET [t0].[Grade] = @p0 FROM [Gadgets] AS [t0]", statement.CommandText);
        Assert.Equal("B", Assert.Single(statement.Parameters).Value);
    }

    [Fact]
    public void Update_ComputesDatesAndConditionalsInSql()
    {
        var statement = Update(
            Query<Gadget>(_gadgets),
            Set(nameof(Gadget.CreatedAt), new FunctionValueTemplate(ValueFunction.AddDays, P(nameof(Gadget.CreatedAt)), S(0)), 3.0),
            Set(nameof(Gadget.Grade), new ConditionalValueTemplate(new ComparisonTemplate(nameof(Gadget.Quantity), ComparisonOperator.GreaterThan, 0), S(1), S(2)), 5, GadgetGrade.A, GadgetGrade.C));

        Assert.StartsWith(
            $"{NoCountOff}UPDATE [t0] SET [t0].[CreatedAt] = DATEADD(day, CAST(@p0 AS int), [t0].[CreatedAt]), [t0].[Grade] = CASE WHEN [t0].[Quantity] > @p1 THEN @p2 ELSE @p3 END FROM [Gadgets] AS [t0]",
            statement.CommandText,
            StringComparison.Ordinal);
        Assert.Equal(["A", "C"], statement.Parameters.Skip(2).Select(p => p.Value));
    }

    [Fact]
    public void Update_ThroughANavigation_JoinsIt()
        => Assert.Equal(
            $"{NoCountOff}UPDATE [t0] SET [t0].[EditorId] = NULL FROM [Book] AS [t0] INNER JOIN [Writer] AS [t1] ON [t0].[WriterId] = [t1].[Id] WHERE [t1].[Name] = @p0",
            Update(Where(Query<Book>(_library), new ComparisonTemplate("Writer.Name", ComparisonOperator.Equal, 0), "Ann"), Value(nameof(Book.EditorId), null)).CommandText);

    [Fact]
    public void Update_IgnoresOrderingAndDistinct()
        => Assert.Equal(
            $"{NoCountOff}UPDATE [t0] SET [t0].[Name] = @p0 FROM {Items} AS [t0]",
            Update(SqlQuery.Distinct(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Name))), Value(nameof(Item.Name), "x")).CommandText);

    [Fact]
    public void Update_AfterPaging_JoinsTheKeysADerivedTableSelects_AndComputesFromTheUpdatedRow()
        => Assert.Equal(
            $"{NoCountOff}UPDATE [t1] SET [t1].[Score] = [t1].[Score] + @p2 FROM {Items} AS [t1] " +
            $"INNER JOIN (SELECT [t0].[Id] FROM {Items} AS [t0] ORDER BY [t0].[Id] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY) AS [t2] ON [t1].[Id] = [t2].[Id]",
            Update(
                SqlQuery.Take(SqlQuery.Skip(SqlQuery.OrderBy(Query<Item>(_items), nameof(Item.Id)), 1), 2),
                Set(nameof(Item.Score), new BinaryValueTemplate(ValueOperator.Add, P(nameof(Item.Score)), S(0)), 1)).CommandText);

    [Fact]
    public void Update_PagedCompositeKey_MatchesEveryKeyPart()
        => Assert.Equal(
            $"{NoCountOff}UPDATE [t1] SET [t1].[Code] = @p1 FROM [Section] AS [t1] INNER JOIN (SELECT TOP(@p0) [t0].[Floor], [t0].[Code] FROM [Section] AS [t0]) AS [t2] " +
            "ON [t1].[Floor] = [t2].[Floor] AND [t1].[Code] = [t2].[Code]",
            Update(SqlQuery.Take(Query<Section>(_library), 1), Value(nameof(Section.Code), "x")).CommandText);

    [Fact]
    public void Update_PagedKeylessEntity_IsNotSupported()
    {
        using var keyless = new KeylessContext();

        Assert.Throws<NotSupportedException>(() => Update(SqlQuery.Take(Query<Report>(keyless), 1), Value(nameof(Report.Title), "x")));
    }

    [Fact]
    public void Update_WithoutSetters_Throws_AsInEfCore()
        => Assert.Throws<InvalidOperationException>(() => Update(Query<Item>(_items)));

    [Fact]
    public void Update_OfAnUnmappedProperty_Throws()
        => Assert.Throws<InvalidOperationException>(() => Update(Query<Item>(_items), Value("Missing", 1)));

    [Fact]
    public void Setter_ChecksItsSlots()
    {
        Assert.Throws<ArgumentException>(() => new UpdateSetter("Name", S(1), [1]));
        Assert.Throws<ArgumentException>(() => new UpdateSetter("", S(0), [1]));
    }

    private static PropertyValueTemplate P(string name) => new(name);

    private static SlotValueTemplate S(int index) => new(index);

    private static UpdateSetter Value(string property, object? value) => new(property, S(0), [value]);

    private static UpdateSetter Set(string property, ValueTemplate value, params object?[] values) => new(property, value, values);

    private static IQueryable<T> Query<T>(DbContext context)
        where T : class
        => SqlQuery.ToSqlQueryable(context.Set<T>());

    private static IQueryable<T> Where<T>(IQueryable<T> source, PredicateTemplate predicate, params object?[] values)
        where T : class
        => SqlQuery.Where(source, predicate, values);

    private static SqlStatement Update<T>(IQueryable<T> query, params UpdateSetter[] setters)
        where T : class
        => Update(query, filtersFor: null, setters);

    private static SqlStatement Update<T>(IQueryable<T> query, Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor, params UpdateSetter[] setters)
        where T : class
    {
        var sql = (SqlQueryable<T>)query;
        var model = sql.Context.Model;
        BoundEntity Bind(Type type) => ModelBinder.GetEntity(model, type);
        var entity = Bind(typeof(T));
        return SqlBuilder.BuildUpdate(
            entity, sql.Tail, SqlServerDialect.Instance, IncludeTree.FromChain(entity, sql.Tail, Bind), filtersFor ?? (static _ => []), setters, Bind);
    }
}
