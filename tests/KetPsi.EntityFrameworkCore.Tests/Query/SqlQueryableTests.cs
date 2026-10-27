using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Query;

public sealed class SqlQueryableTests : IDisposable
{
    private readonly TestDbContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ElementType_IsEntityType()
        => Assert.Equal(typeof(Blog), CreateQuery().ElementType);

    [Fact]
    public void Expression_IsConstantOfQuery()
    {
        var query = CreateQuery();

        var constant = Assert.IsAssignableFrom<ConstantExpression>(query.Expression);
        Assert.Same(query, constant.Value);
    }

    [Fact]
    public void Provider_IsSqlQueryProvider()
        => Assert.Same(SqlQueryProvider.Instance, CreateQuery().Provider);

    [Fact]
    public void WithTail_ReturnsNewQueryWithSameContext()
    {
        var query = CreateQuery();
        var tail = new TakeNode(query.Tail, 1);

        var result = query.WithTail(tail);

        Assert.NotSame(query, result);
        Assert.Same(query.Context, result.Context);
        Assert.Same(tail, result.Tail);
    }

    [Fact]
    public void Root_IsFirstNodeOfChain()
    {
        var query = CreateQuery();
        var root = query.Tail;

        var longer = SqlQuery.ToSqlQueryable(SqlQuery.Take(SqlQuery.AsNoTracking(query), 3));

        Assert.Same(root, longer.Root);
        Assert.Same(root, query.Root);
    }

    private SqlQueryable<Blog> CreateQuery() => SqlQuery.ToSqlQueryable(_db.Blogs);
}
