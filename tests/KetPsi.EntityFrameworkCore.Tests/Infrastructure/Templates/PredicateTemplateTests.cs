using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure.Templates;

public sealed class PredicateTemplateTests
{
    [Fact]
    public void Comparison_StoresMembersAndCountsSlotsUpToIndex()
    {
        var template = new ComparisonTemplate("Rating", ComparisonOperator.GreaterThan, valueIndex: 2);

        Assert.Equal("Rating", template.PropertyName);
        Assert.Equal(ComparisonOperator.GreaterThan, template.Comparison);
        Assert.Equal(2, template.ValueIndex);
        Assert.Equal(3, template.ValueCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Comparison_MissingPropertyName_Throws(string? propertyName)
        => Assert.ThrowsAny<ArgumentException>(() => new ComparisonTemplate(propertyName!, ComparisonOperator.Equal, 0));

    [Fact]
    public void Comparison_NegativeValueIndex_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>("valueIndex", () => new ComparisonTemplate("Rating", ComparisonOperator.Equal, -1));

    [Fact]
    public void Comparison_UndefinedOperator_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>("comparison", () => new ComparisonTemplate("Rating", (ComparisonOperator)99, 0));

    [Fact]
    public void BooleanProperty_HasNoSlots()
    {
        var template = new BooleanPropertyTemplate("IsActive");

        Assert.Equal("IsActive", template.PropertyName);
        Assert.Equal(0, template.ValueCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BooleanProperty_MissingPropertyName_Throws(string? propertyName)
        => Assert.ThrowsAny<ArgumentException>(() => new BooleanPropertyTemplate(propertyName!));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullCheck_StoresDirectionAndHasNoSlots(bool isNull)
    {
        var template = new NullCheckTemplate("Description", isNull);

        Assert.Equal("Description", template.PropertyName);
        Assert.Equal(isNull, template.IsNull);
        Assert.Equal(0, template.ValueCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullCheck_MissingPropertyName_Throws(string? propertyName)
        => Assert.ThrowsAny<ArgumentException>(() => new NullCheckTemplate(propertyName!, isNull: true));

    [Fact]
    public void InList_StoresMembersAndCountsSlotsUpToIndex()
    {
        var template = new InListTemplate("Id", valueIndex: 1);

        Assert.Equal("Id", template.PropertyName);
        Assert.Equal(1, template.ValueIndex);
        Assert.Equal(2, template.ValueCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void InList_MissingPropertyName_Throws(string? propertyName)
        => Assert.ThrowsAny<ArgumentException>(() => new InListTemplate(propertyName!, 0));

    [Fact]
    public void InList_NegativeValueIndex_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>("valueIndex", () => new InListTemplate("Id", -1));

    [Fact]
    public void Not_TakesOperandSlotCount()
    {
        var operand = new ComparisonTemplate("Rating", ComparisonOperator.Equal, 1);

        var template = new NotTemplate(operand);

        Assert.Same(operand, template.Operand);
        Assert.Equal(2, template.ValueCount);
    }

    [Fact]
    public void Not_NullOperand_Throws()
        => Assert.Throws<ArgumentNullException>("operand", () => new NotTemplate(null!));

    [Fact]
    public void And_TakesHighestSlotCountOfOperands()
    {
        var left = new ComparisonTemplate("Rating", ComparisonOperator.Equal, 3);
        var right = new ComparisonTemplate("Id", ComparisonOperator.Equal, 0);

        var template = new AndTemplate(left, right);

        Assert.Same(left, template.Left);
        Assert.Same(right, template.Right);
        Assert.Equal(4, template.ValueCount);
    }

    [Fact]
    public void Or_TakesHighestSlotCountOfOperands()
    {
        var left = new BooleanPropertyTemplate("IsActive");
        var right = new InListTemplate("Id", 2);

        var template = new OrTemplate(left, right);

        Assert.Same(left, template.Left);
        Assert.Same(right, template.Right);
        Assert.Equal(3, template.ValueCount);
    }

    [Fact]
    public void And_NullOperand_Throws()
    {
        var operand = new BooleanPropertyTemplate("IsActive");

        Assert.Throws<ArgumentNullException>("left", () => new AndTemplate(null!, operand));
        Assert.Throws<ArgumentNullException>("right", () => new AndTemplate(operand, null!));
    }

    [Fact]
    public void Or_NullOperand_Throws()
    {
        var operand = new BooleanPropertyTemplate("IsActive");

        Assert.Throws<ArgumentNullException>("left", () => new OrTemplate(null!, operand));
        Assert.Throws<ArgumentNullException>("right", () => new OrTemplate(operand, null!));
    }
}
