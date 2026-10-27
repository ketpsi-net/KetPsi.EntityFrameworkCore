using System.Linq.Expressions;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure;

public sealed class CapturedValuesTests
{
    private static int s_limit = 7;

    private readonly int _instanceLimit = 9;

    [Fact]
    public void Read_Constant_ReturnsValue()
        => Assert.Equal(3, CapturedValues.Read(RightOf(b => b.Rating > 3)));

    [Fact]
    public void Read_CapturedLocal_ReadsClosureField()
    {
        var min = 5;

        Assert.Equal(5, CapturedValues.Read(RightOf(b => b.Rating > min)));
    }

    [Fact]
    public void Read_CapturedLocal_SeesValueAtReadTime()
    {
        var min = 5;
        var node = RightOf(b => b.Rating > min);
        min = 6;

        Assert.Equal(6, CapturedValues.Read(node));
    }

    [Fact]
    public void Read_PropertyChainOnCapturedObject_ReadsEachMember()
    {
        var request = new Request(new Filter("abc"));

        Assert.Equal("abc", CapturedValues.Read(RightOf(b => b.Name == request.Filter.Name)));
    }

    [Fact]
    public void Read_InstanceField_ReadsThroughThis()
        => Assert.Equal(9, CapturedValues.Read(RightOf(b => b.Rating > _instanceLimit)));

    [Fact]
    public void Read_StaticField_HasNoTarget()
        => Assert.Equal(7, CapturedValues.Read(RightOf(b => b.Rating > s_limit)));

    [Fact]
    public void Read_StaticProperty_HasNoTarget()
        => Assert.Equal(DateTime.MaxValue.Year, CapturedValues.Read(RightOf(b => b.Rating > DateTime.MaxValue.Year)));

    [Fact]
    public void Read_LiftedToNullable_UnwrapsConversion()
    {
        var value = 4;
        Expression<Func<Item, bool>> predicate = i => i.Score == value;

        var right = ((BinaryExpression)predicate.Body).Right;

        Assert.Equal(ExpressionType.Convert, right.NodeType);
        Assert.Equal(4, CapturedValues.Read(right));
    }

    [Fact]
    public void Read_NullValue_ReturnsNull()
    {
        string? name = null;

        Assert.Null(CapturedValues.Read(RightOf(b => b.Name == name)));
    }

    [Fact]
    public void Read_SpanImplicitConversion_ReadsArray()
    {
        int[] ids = [1, 2];
        var method = typeof(ReadOnlySpan<int>).GetMethod("op_Implicit", [typeof(int[])])!;
        var call = Expression.Call(method, Expression.Constant(ids));

        Assert.Same(ids, CapturedValues.Read(call));
    }

    [Fact]
    public void Read_UserDefinedConversion_Throws()
    {
        var method = typeof(Money).GetMethod("op_Implicit")!;
        var node = Expression.Convert(Expression.Constant(new Money(1m)), typeof(decimal), method);

        Assert.Throws<NotSupportedException>(() => CapturedValues.Read(node));
    }

    [Fact]
    public void Read_OtherCall_ThrowsNamingNode()
    {
        var node = Expression.Call(typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!, Expression.Constant(-1));

        var exception = Assert.Throws<NotSupportedException>(() => CapturedValues.Read(node));

        Assert.Contains(nameof(ExpressionType.Call), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Arithmetic_Throws()
        => Assert.Throws<NotSupportedException>(() => CapturedValues.Read(Expression.Add(Expression.Constant(1), Expression.Constant(2))));

    [Fact]
    public void Read_Null_Throws()
        => Assert.Throws<ArgumentNullException>("node", () => CapturedValues.Read(null!));

    private static Expression RightOf(Expression<Func<Blog, bool>> predicate) => ((BinaryExpression)predicate.Body).Right;

    private sealed record Request(Filter Filter);

    private sealed record Filter(string Name);

    private readonly record struct Money(decimal Amount)
    {
        public static implicit operator decimal(Money money) => money.Amount;
    }
}
