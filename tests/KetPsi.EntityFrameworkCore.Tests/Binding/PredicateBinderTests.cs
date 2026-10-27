using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Binding;

public sealed class PredicateBinderTests
{
    private readonly BoundEntity _entity = ModelBinderTests.Bind<ProductContext, Product>();

    [Fact]
    public void Comparison_BindsPropertyToColumn()
    {
        var bound = _entity.BindPredicate(new ComparisonTemplate(nameof(Product.Name), ComparisonOperator.NotEqual, 1));

        Assert.Equal(new BoundComparison(Property(nameof(Product.Name)), ComparisonOperator.NotEqual, 1), bound);
        Assert.Equal("ProductName", ((BoundComparison)bound).Property.ColumnName);
    }

    [Fact]
    public void BooleanProperty_BindsBooleanColumn()
        => Assert.Equal(
            new BoundBooleanProperty(Property(nameof(Product.IsActive))),
            _entity.BindPredicate(new BooleanPropertyTemplate(nameof(Product.IsActive))));

    [Fact]
    public void BooleanProperty_NonBooleanProperty_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => _entity.BindPredicate(new BooleanPropertyTemplate(nameof(Product.Stock))));

        Assert.Contains(nameof(Product.Stock), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullCheck_BindsDirection(bool isNull)
        => Assert.Equal(
            new BoundNullCheck(Property(nameof(Product.Description)), isNull),
            _entity.BindPredicate(new NullCheckTemplate(nameof(Product.Description), isNull)));

    [Fact]
    public void InList_BindsPropertyAndSlot()
        => Assert.Equal(
            new BoundInList(Property(nameof(Product.Id)), 0),
            _entity.BindPredicate(new InListTemplate(nameof(Product.Id), 0)));

    [Fact]
    public void CompositeTemplate_BindsEveryOperand()
    {
        var template = new OrTemplate(
            new AndTemplate(
                new BooleanPropertyTemplate(nameof(Product.IsActive)),
                new ComparisonTemplate(nameof(Product.Stock), ComparisonOperator.GreaterThan, 0)),
            new NotTemplate(new NullCheckTemplate(nameof(Product.Description), isNull: true)));

        var expected = new BoundOr(
            new BoundAnd(
                new BoundBooleanProperty(Property(nameof(Product.IsActive))),
                new BoundComparison(Property(nameof(Product.Stock)), ComparisonOperator.GreaterThan, 0)),
            new BoundNot(new BoundNullCheck(Property(nameof(Product.Description)), IsNull: true)));

        Assert.Equal(expected, _entity.BindPredicate(template));
    }

    [Fact]
    public void UnknownProperty_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => _entity.BindPredicate(new ComparisonTemplate("Missing", ComparisonOperator.Equal, 0)));

    [Fact]
    public void UnknownPropertyInsideComposite_Throws()
        => Assert.Throws<InvalidOperationException>(() => _entity.BindPredicate(
            new AndTemplate(new BooleanPropertyTemplate(nameof(Product.IsActive)), new NullCheckTemplate("Missing", isNull: true))));

    [Fact]
    public void BindPredicate_SameTemplate_ReturnsCachedInstance()
    {
        var template = new ComparisonTemplate(nameof(Product.Id), ComparisonOperator.Equal, 0);

        Assert.Same(_entity.BindPredicate(template), _entity.BindPredicate(template));
    }

    [Fact]
    public void BindPredicate_EqualButDistinctTemplates_BindSeparately()
    {
        var first = _entity.BindPredicate(new ComparisonTemplate(nameof(Product.Id), ComparisonOperator.Equal, 0));
        var second = _entity.BindPredicate(new ComparisonTemplate(nameof(Product.Id), ComparisonOperator.Equal, 0));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void BindPredicate_NullTemplate_Throws()
        => Assert.Throws<ArgumentNullException>("template", () => _entity.BindPredicate(null!));

    private BoundProperty Property(string name) => _entity.GetProperty(name);
}
