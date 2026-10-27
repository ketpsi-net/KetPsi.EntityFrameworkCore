using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public sealed class SqlBuilderPredicateTests : SqlBuilderTestBase
{
    private const string InIds = "[t0].[Id] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$'))";
    private const string InScores = "[t0].[Score] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$'))";

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "[t0].[Id] = @p0")]
    [InlineData(ComparisonOperator.NotEqual, false, "[t0].[Id] <> @p0")]
    [InlineData(ComparisonOperator.LessThan, false, "[t0].[Id] < @p0")]
    [InlineData(ComparisonOperator.LessThanOrEqual, false, "[t0].[Id] <= @p0")]
    [InlineData(ComparisonOperator.GreaterThan, false, "[t0].[Id] > @p0")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, false, "[t0].[Id] >= @p0")]
    [InlineData(ComparisonOperator.Equal, true, "[t0].[Id] <> @p0")]
    [InlineData(ComparisonOperator.NotEqual, true, "[t0].[Id] = @p0")]
    [InlineData(ComparisonOperator.LessThan, true, "[t0].[Id] >= @p0")]
    [InlineData(ComparisonOperator.LessThanOrEqual, true, "[t0].[Id] > @p0")]
    [InlineData(ComparisonOperator.GreaterThan, true, "[t0].[Id] <= @p0")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, true, "[t0].[Id] < @p0")]
    public void Comparison_NonNullableColumn_NeedsNoNullHandling(ComparisonOperator comparison, bool negated, string expected)
    {
        var statement = BuildWhere(Negate(new ComparisonTemplate(nameof(Item.Id), comparison, 0), negated), 5);

        Assert.Equal(SelectAll + " WHERE " + expected, statement.CommandText);
        Assert.Equal([new QueryParameter("@p0", 5, Property(nameof(Item.Id)))], statement.Parameters);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "[t0].[Score] = @p0")]
    [InlineData(ComparisonOperator.NotEqual, false, "([t0].[Score] <> @p0 OR [t0].[Score] IS NULL)")]
    [InlineData(ComparisonOperator.LessThan, false, "[t0].[Score] < @p0")]
    [InlineData(ComparisonOperator.LessThanOrEqual, false, "[t0].[Score] <= @p0")]
    [InlineData(ComparisonOperator.GreaterThan, false, "[t0].[Score] > @p0")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, false, "[t0].[Score] >= @p0")]
    [InlineData(ComparisonOperator.Equal, true, "([t0].[Score] <> @p0 OR [t0].[Score] IS NULL)")]
    [InlineData(ComparisonOperator.NotEqual, true, "[t0].[Score] = @p0")]
    [InlineData(ComparisonOperator.LessThan, true, "([t0].[Score] >= @p0 OR [t0].[Score] IS NULL)")]
    [InlineData(ComparisonOperator.LessThanOrEqual, true, "([t0].[Score] > @p0 OR [t0].[Score] IS NULL)")]
    [InlineData(ComparisonOperator.GreaterThan, true, "([t0].[Score] <= @p0 OR [t0].[Score] IS NULL)")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, true, "([t0].[Score] < @p0 OR [t0].[Score] IS NULL)")]
    public void Comparison_NullableColumn_MatchesNullRowsWhereCSharpDoes(ComparisonOperator comparison, bool negated, string expected)
    {
        var statement = BuildWhere(Negate(new ComparisonTemplate(nameof(Item.Score), comparison, 0), negated), 5);

        Assert.Equal(SelectAll + " WHERE " + expected, statement.CommandText);
        Assert.Equal([new QueryParameter("@p0", 5, Property(nameof(Item.Score)))], statement.Parameters);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "[t0].[Score] IS NULL")]
    [InlineData(ComparisonOperator.NotEqual, false, "[t0].[Score] IS NOT NULL")]
    [InlineData(ComparisonOperator.LessThan, false, "0 = 1")]
    [InlineData(ComparisonOperator.LessThanOrEqual, false, "0 = 1")]
    [InlineData(ComparisonOperator.GreaterThan, false, "0 = 1")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, false, "0 = 1")]
    [InlineData(ComparisonOperator.Equal, true, "[t0].[Score] IS NOT NULL")]
    [InlineData(ComparisonOperator.NotEqual, true, "[t0].[Score] IS NULL")]
    [InlineData(ComparisonOperator.LessThan, true, "1 = 1")]
    [InlineData(ComparisonOperator.LessThanOrEqual, true, "1 = 1")]
    [InlineData(ComparisonOperator.GreaterThan, true, "1 = 1")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, true, "1 = 1")]
    public void Comparison_NullValue_IsDecidedWithoutParameter(ComparisonOperator comparison, bool negated, string expected)
    {
        var statement = BuildWhere(Negate(new ComparisonTemplate(nameof(Item.Score), comparison, 0), negated), [null]);

        Assert.Equal(SelectAll + " WHERE " + expected, statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public void Comparison_SameTemplateWithNullAndNonNullValues_BuildsDifferentSql()
    {
        var template = new ComparisonTemplate(nameof(Item.Score), ComparisonOperator.Equal, 0);

        Assert.Equal(SelectAll + " WHERE [t0].[Score] IS NULL", BuildWhere(template, [null]).CommandText);
        Assert.Equal(SelectAll + " WHERE [t0].[Score] = @p0", BuildWhere(template, 3).CommandText);
    }

    [Theory]
    [InlineData(false, "[t0].[IsActive] = CAST(1 AS bit)")]
    [InlineData(true, "[t0].[IsActive] = CAST(0 AS bit)")]
    public void BooleanProperty_ComparesWithBitLiteral(bool negated, string expected)
    {
        var statement = BuildWhere(Negate(new BooleanPropertyTemplate(nameof(Item.IsActive)), negated));

        Assert.Equal(SelectAll + " WHERE " + expected, statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Theory]
    [InlineData(true, false, "[t0].[Score] IS NULL")]
    [InlineData(false, false, "[t0].[Score] IS NOT NULL")]
    [InlineData(true, true, "[t0].[Score] IS NOT NULL")]
    [InlineData(false, true, "[t0].[Score] IS NULL")]
    public void NullCheck_RendersIsNull(bool isNull, bool negated, string expected)
        => Assert.Equal(
            SelectAll + " WHERE " + expected,
            BuildWhere(Negate(new NullCheckTemplate(nameof(Item.Score), isNull), negated)).CommandText);

    [Fact]
    public void InList_PassesNonNullValuesAsOneJsonParameter()
    {
        var statement = BuildWhere(new InListTemplate(nameof(Item.Id), 0), new List<int> { 1, 2 });

        Assert.Equal(SelectAll + " WHERE " + InIds, statement.CommandText);
        Assert.Equal([new QueryParameter("@p0", "[1,2]", Property: null)], statement.Parameters);
    }

    [Fact]
    public void InList_Negated_UsesNot()
        => Assert.Equal(
            SelectAll + " WHERE NOT (" + InIds + ")",
            BuildWhere(new NotTemplate(new InListTemplate(nameof(Item.Id), 0)), new[] { 1, 2 }).CommandText);

    [Fact]
    public void InList_NullInListForNonNullableColumn_IsDropped()
    {
        var statement = BuildWhere(new InListTemplate(nameof(Item.Id), 0), new int?[] { 1, null });

        Assert.Equal(SelectAll + " WHERE " + InIds, statement.CommandText);
        Assert.Equal("[1]", statement.Parameters[0].Value);
    }

    [Theory]
    [InlineData(false, false, InScores)]
    [InlineData(false, true, "(" + InScores + " OR [t0].[Score] IS NULL)")]
    [InlineData(true, false, "(NOT (" + InScores + ") OR [t0].[Score] IS NULL)")]
    [InlineData(true, true, "NOT (" + InScores + ")")]
    public void InList_NullableColumn_MatchesNullRowsWhereCSharpDoes(bool negated, bool listHasNull, string expected)
    {
        int?[] list = listHasNull ? [1, null] : [1];

        var statement = BuildWhere(Negate(new InListTemplate(nameof(Item.Score), 0), negated), list);

        Assert.Equal(SelectAll + " WHERE " + expected, statement.CommandText);
        Assert.Equal("[1]", statement.Parameters[0].Value);
    }

    [Fact]
    public void InList_EmptyList_PassesEmptyArray()
        => Assert.Equal("[]", BuildWhere(new InListTemplate(nameof(Item.Id), 0), Array.Empty<int>()).Parameters[0].Value);

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    public void InList_ValueIsNotCollection_Throws(object? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildWhere(new InListTemplate(nameof(Item.Id), 0), value));

        Assert.Contains(nameof(Item.Id), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void And_RendersParenthesizedConjunction()
        => Assert.Equal(
            SelectAll + " WHERE ([t0].[Id] = @p0 AND [t0].[IsActive] = CAST(1 AS bit))",
            BuildWhere(new AndTemplate(Equal(nameof(Item.Id), 0), new BooleanPropertyTemplate(nameof(Item.IsActive))), 1).CommandText);

    [Fact]
    public void Or_InsideAnd_KeepsGrouping()
    {
        var template = new AndTemplate(
            new OrTemplate(Equal(nameof(Item.Id), 0), Equal(nameof(Item.Id), 1)),
            new BooleanPropertyTemplate(nameof(Item.IsActive)));

        var statement = BuildWhere(template, 1, 2);

        Assert.Equal(
            SelectAll + " WHERE (([t0].[Id] = @p0 OR [t0].[Id] = @p1) AND [t0].[IsActive] = CAST(1 AS bit))",
            statement.CommandText);
        Assert.Equal([1, 2], statement.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void NotOverAnd_AppliesDeMorganWithNullHandling()
        => Assert.Equal(
            SelectAll + " WHERE ([t0].[Id] <> @p0 OR ([t0].[Score] <> @p1 OR [t0].[Score] IS NULL))",
            BuildWhere(new NotTemplate(new AndTemplate(Equal(nameof(Item.Id), 0), Equal(nameof(Item.Score), 1))), 1, 2).CommandText);

    [Fact]
    public void NotOverOr_AppliesDeMorgan()
        => Assert.Equal(
            SelectAll + " WHERE ([t0].[IsActive] = CAST(0 AS bit) AND [t0].[Name] <> @p0)",
            BuildWhere(new NotTemplate(new OrTemplate(new BooleanPropertyTemplate(nameof(Item.IsActive)), Equal(nameof(Item.Name), 0))), "x").CommandText);

    [Fact]
    public void DoubleNegation_CancelsOut()
        => Assert.Equal(
            SelectAll + " WHERE [t0].[Score] = @p0",
            BuildWhere(new NotTemplate(new NotTemplate(Equal(nameof(Item.Score), 0))), 1).CommandText);

    [Fact]
    public void SharedSlot_IsReadByEachComparison()
    {
        var template = new OrTemplate(Equal(nameof(Item.Id), 0), Equal(nameof(Item.Score), 0));

        var statement = BuildWhere(template, 7);

        Assert.Equal(SelectAll + " WHERE ([t0].[Id] = @p0 OR [t0].[Score] = @p1)", statement.CommandText);
        Assert.Equal([7, 7], statement.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void SeveralWheres_AreJoinedWithAnd()
    {
        var query = Where(Where(Items, Equal(nameof(Item.Id), 0), 1), Equal(nameof(Item.Name), 0), "a");

        var statement = Build(query);

        Assert.Equal(SelectAll + " WHERE [t0].[Id] = @p0 AND [t0].[Name] = @p1", statement.CommandText);
        Assert.Equal(
            [new QueryParameter("@p0", 1, Property(nameof(Item.Id))), new QueryParameter("@p1", "a", Property(nameof(Item.Name)))],
            statement.Parameters);
    }

    private static ComparisonTemplate Equal(string propertyName, int valueIndex)
        => new(propertyName, ComparisonOperator.Equal, valueIndex);

    private static PredicateTemplate Negate(PredicateTemplate template, bool negated)
        => negated ? new NotTemplate(template) : template;

    private SqlStatement BuildWhere(PredicateTemplate template, params object?[] values)
        => Build(Where(Items, template, values));
}
