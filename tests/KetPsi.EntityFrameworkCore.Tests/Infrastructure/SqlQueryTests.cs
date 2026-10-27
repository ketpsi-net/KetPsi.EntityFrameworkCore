using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure;

public sealed class SqlQueryTests : IDisposable
{
    private readonly TestDbContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void IsInterceptable_DbSet_ReturnsTrue()
        => Assert.True(SqlQuery.IsInterceptable(_db.Blogs));

    [Fact]
    public void IsInterceptable_SqlQueryable_ReturnsTrue()
        => Assert.True(SqlQuery.IsInterceptable(SqlQuery.AsNoTracking(_db.Blogs)));

    [Fact]
    public void IsInterceptable_LinqToObjects_ReturnsFalse()
        => Assert.False(SqlQuery.IsInterceptable(new List<Blog>().AsQueryable()));

    [Fact]
    public void IsInterceptable_EfQueryFromUninterceptedOperator_Throws()
    {
        var efQuery = _db.Blogs.AsNoTracking();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.IsInterceptable(efQuery));
        Assert.Equal(ExceptionMessages.UninterceptedEntityFrameworkOperator, exception.Message);
    }

    [Fact]
    public void IsInterceptable_NullSource_Throws()
        => Assert.Throws<ArgumentNullException>("source", () => SqlQuery.IsInterceptable<Blog>(null!));

    [Fact]
    public void ToSqlQueryable_DbSet_CreatesRootWithContextAndEntityType()
    {
        var query = SqlQuery.ToSqlQueryable(_db.Blogs);

        Assert.Same(_db, query.Context);
        var root = Assert.IsType<RootNode>(query.Tail);
        Assert.Equal(typeof(Blog), root.EntityType);
        Assert.Null(root.Parent);
    }

    [Fact]
    public void ToSqlQueryable_SqlQueryable_ReturnsSameInstance()
    {
        var query = SqlQuery.ToSqlQueryable(_db.Blogs);

        Assert.Same(query, SqlQuery.ToSqlQueryable(query));
    }

    [Fact]
    public void ToSqlQueryable_OtherQueryable_ThrowsNamingSourceType()
    {
        var source = new List<Blog>().AsQueryable();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlQuery.ToSqlQueryable(source));
        Assert.Contains(source.GetType().ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSqlQueryable_NullSource_Throws()
        => Assert.Throws<ArgumentNullException>("source", () => SqlQuery.ToSqlQueryable<Blog>(null!));

    [Fact]
    public void Where_AppendsPredicateWithValues()
    {
        var predicate = new ComparisonTemplate(nameof(Blog.Rating), ComparisonOperator.GreaterThan, 0);
        object?[] values = [3];

        var query = AsSql(SqlQuery.Where(_db.Blogs, predicate, values));

        var node = Assert.IsType<WhereNode>(query.Tail);
        Assert.Same(predicate, node.Predicate);
        Assert.Same(values, node.Values);
        Assert.IsType<RootNode>(node.Parent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Where_WrongValueCount_Throws(int valueCount)
    {
        var predicate = new ComparisonTemplate(nameof(Blog.Rating), ComparisonOperator.GreaterThan, 0);

        var exception = Assert.Throws<ArgumentException>("values", () => SqlQuery.Where(_db.Blogs, predicate, new object?[valueCount]));

        Assert.Contains($"{valueCount} were supplied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_NullArguments_Throw()
    {
        var predicate = new BooleanPropertyTemplate("IsActive");

        Assert.Throws<ArgumentNullException>("predicate", () => SqlQuery.Where(_db.Blogs, null!, []));
        Assert.Throws<ArgumentNullException>("values", () => SqlQuery.Where(_db.Blogs, predicate, null!));
    }

    [Fact]
    public void WhereIf_True_AppendsPredicate()
    {
        var predicate = new ComparisonTemplate(nameof(Blog.Rating), ComparisonOperator.Equal, 0);

        var query = AsSql(SqlQuery.WhereIf(_db.Blogs, condition: true, predicate, [5]));

        Assert.Same(predicate, Assert.IsType<WhereNode>(query.Tail).Predicate);
    }

    [Fact]
    public void WhereIf_False_ReturnsChainUnchanged()
    {
        var source = AsSql(SqlQuery.AsNoTracking(_db.Blogs));
        var predicate = new ComparisonTemplate(nameof(Blog.Rating), ComparisonOperator.Equal, 0);

        var query = AsSql(SqlQuery.WhereIf(source, condition: false, predicate, [5]));

        Assert.Same(source.Tail, query.Tail);
    }

    [Fact]
    public void WhereIf_FalseOnDbSet_StillReturnsKetPsiQuery()
    {
        var predicate = new ComparisonTemplate(nameof(Blog.Rating), ComparisonOperator.Equal, 0);

        var query = AsSql(SqlQuery.WhereIf(_db.Blogs, condition: false, predicate, [5]));

        Assert.IsType<RootNode>(query.Tail);
    }

    [Fact]
    public void AsNoTracking_AppendsNoTrackingNode()
    {
        var query = AsSql(SqlQuery.AsNoTracking(_db.Blogs));

        var node = Assert.IsType<NoTrackingNode>(query.Tail);
        Assert.IsType<RootNode>(node.Parent);
    }

    [Fact]
    public void IgnoreQueryFilters_WithoutKeys_IgnoresAllFilters()
    {
        var query = AsSql(SqlQuery.IgnoreQueryFilters(_db.Blogs));

        var node = Assert.IsType<IgnoreQueryFiltersNode>(query.Tail);
        Assert.Null(node.FilterKeys);
    }

    [Fact]
    public void IgnoreQueryFilters_WithKeys_CopiesKeys()
    {
        var keys = new List<string> { "SoftDelete", "Tenant" };

        var query = AsSql(SqlQuery.IgnoreQueryFilters(_db.Blogs, keys));
        keys.Add("Later");

        var node = Assert.IsType<IgnoreQueryFiltersNode>(query.Tail);
        Assert.Equal(["SoftDelete", "Tenant"], node.FilterKeys);
    }

    [Fact]
    public void IgnoreQueryFilters_NullKeys_Throws()
        => Assert.Throws<ArgumentNullException>("filterKeys", () => SqlQuery.IgnoreQueryFilters(_db.Blogs, null!));

    [Fact]
    public void OrderBy_AppendsAscendingOrder()
        => AssertOrder(SqlQuery.OrderBy(_db.Blogs, nameof(Blog.Name)), nameof(Blog.Name), descending: false, isThenBy: false);

    [Fact]
    public void OrderByDescending_AppendsDescendingOrder()
        => AssertOrder(SqlQuery.OrderByDescending(_db.Blogs, nameof(Blog.Name)), nameof(Blog.Name), descending: true, isThenBy: false);

    [Fact]
    public void ThenBy_AppendsAscendingThenBy()
    {
        var ordered = SqlQuery.OrderBy(_db.Blogs, nameof(Blog.Name));

        AssertOrder(SqlQuery.ThenBy(ordered, nameof(Blog.Rating)), nameof(Blog.Rating), descending: false, isThenBy: true);
    }

    [Fact]
    public void ThenByDescending_AppendsDescendingThenBy()
    {
        var ordered = SqlQuery.OrderBy(_db.Blogs, nameof(Blog.Name));

        AssertOrder(SqlQuery.ThenByDescending(ordered, nameof(Blog.Rating)), nameof(Blog.Rating), descending: true, isThenBy: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OrderBy_MissingPropertyName_Throws(string? propertyName)
        => Assert.ThrowsAny<ArgumentException>(() => SqlQuery.OrderBy(_db.Blogs, propertyName!));

    [Fact]
    public void Skip_AppendsSkipNode()
    {
        var query = AsSql(SqlQuery.Skip(_db.Blogs, 5));

        Assert.Equal(5, Assert.IsType<SkipNode>(query.Tail).Count);
    }

    [Fact]
    public void Take_AppendsTakeNode()
    {
        var query = AsSql(SqlQuery.Take(_db.Blogs, 3));

        Assert.Equal(3, Assert.IsType<TakeNode>(query.Tail).Count);
    }

    [Fact]
    public void Append_KeepsContextAndLeavesSourceUnchanged()
    {
        var source = AsSql(SqlQuery.AsNoTracking(_db.Blogs));
        var sourceTail = source.Tail;

        var appended = AsSql(SqlQuery.Take(source, 10));

        Assert.NotSame(source, appended);
        Assert.Same(sourceTail, source.Tail);
        Assert.Same(source.Context, appended.Context);
    }

    [Fact]
    public void Fork_QueriesFromSharedBase_DoNotAffectEachOther()
    {
        var shared = AsSql(SqlQuery.OrderBy(SqlQuery.AsNoTracking(_db.Blogs), nameof(Blog.Name)));

        var skipped = AsSql(SqlQuery.Skip(shared, 5));
        var taken = AsSql(SqlQuery.Take(shared, 3));

        Assert.Same(shared.Tail, skipped.Tail.Parent);
        Assert.Same(shared.Tail, taken.Tail.Parent);
        Assert.Equal(5, Assert.IsType<SkipNode>(skipped.Tail).Count);
        Assert.Equal(3, Assert.IsType<TakeNode>(taken.Tail).Count);
        Assert.IsType<OrderNode>(shared.Tail);
    }

    [Fact]
    public void Chain_PreservesOperatorOrder()
    {
        var query = AsSql(SqlQuery.Take(SqlQuery.Skip(SqlQuery.AsNoTracking(_db.Blogs), 2), 4));

        var take = Assert.IsType<TakeNode>(query.Tail);
        var skip = Assert.IsType<SkipNode>(take.Parent);
        var noTracking = Assert.IsType<NoTrackingNode>(skip.Parent);
        Assert.IsType<RootNode>(noTracking.Parent);
    }

    private static SqlQueryable<Blog> AsSql(IQueryable<Blog> query) => Assert.IsType<SqlQueryable<Blog>>(query);

    private static void AssertOrder(IOrderedQueryable<Blog> query, string propertyName, bool descending, bool isThenBy)
    {
        var node = Assert.IsType<OrderNode>(AsSql(query).Tail);
        Assert.Equal(propertyName, node.PropertyName);
        Assert.Equal(descending, node.Descending);
        Assert.Equal(isThenBy, node.IsThenBy);
    }
}
