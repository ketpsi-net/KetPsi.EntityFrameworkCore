using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Query;

public sealed class SqlQueryProviderTests : IDisposable
{
    private readonly TestDbContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void GenericCreateQuery_UninterceptedOperator_ThrowsNamingOperator()
    {
        var query = CreateQuery();

        var exception = Assert.Throws<NotSupportedException>(() => query.Where(b => b.Rating > 3));
        Assert.Contains("'Where'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericExecute_UninterceptedTerminal_ThrowsNamingOperator()
    {
        var query = CreateQuery();

        var exception = Assert.Throws<NotSupportedException>(() => query.Count());
        Assert.Contains("'Count'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonGenericCreateQuery_Throws()
    {
        var query = CreateQuery();

        Assert.Throws<NotSupportedException>(() => query.Provider.CreateQuery(query.Expression));
    }

    [Fact]
    public void NonGenericExecute_Throws()
    {
        var query = CreateQuery();

        Assert.Throws<NotSupportedException>(() => query.Provider.Execute(query.Expression));
    }

    [Fact]
    public void Throw_NonMethodExpression_NamesNodeType()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => SqlQueryProvider.Instance.Execute<int>(Expression.Constant(1)));

        Assert.Contains($"'{nameof(ExpressionType.Constant)}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Throw_NullExpression_ThrowsArgumentNull()
        => Assert.Throws<ArgumentNullException>("expression", () => SqlQueryProvider.Instance.Execute(null!));

    private SqlQueryable<Blog> CreateQuery() => SqlQuery.ToSqlQueryable(_db.Blogs);
}
