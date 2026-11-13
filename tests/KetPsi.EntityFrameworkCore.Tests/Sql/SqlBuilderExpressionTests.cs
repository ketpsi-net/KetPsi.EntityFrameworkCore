using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>
/// Predicates and orderings on computed values, written as EF Core writes them on SQL Server (probed on EF Core 9, 10
/// and 11), and true exactly where C# is.
/// </summary>
public sealed class SqlBuilderExpressionTests : IDisposable
{
    private const string From = " FROM [Gadgets] AS [t0]";

    private static readonly ValueTemplate Name = P(nameof(Gadget.Name));
    private static readonly ValueTemplate Code = P(nameof(Gadget.Code));
    private static readonly ValueTemplate Note = P(nameof(Gadget.Note));
    private static readonly ValueTemplate Price = P(nameof(Gadget.Price));
    private static readonly ValueTemplate Cost = P(nameof(Gadget.Cost));
    private static readonly ValueTemplate Quantity = P(nameof(Gadget.Quantity));
    private static readonly ValueTemplate Discount = P(nameof(Gadget.Discount));
    private static readonly ValueTemplate Stock = P(nameof(Gadget.Stock));
    private static readonly ValueTemplate Views = P(nameof(Gadget.Views));
    private static readonly ValueTemplate Rating = P(nameof(Gadget.Rating));
    private static readonly ValueTemplate IsActive = P(nameof(Gadget.IsActive));
    private static readonly ValueTemplate IsFeatured = P(nameof(Gadget.IsFeatured));
    private static readonly ValueTemplate Status = P(nameof(Gadget.Status));
    private static readonly ValueTemplate Grade = P(nameof(Gadget.Grade));

    private readonly GadgetContext _db = new();

    public void Dispose() => _db.Dispose();

    // --- Two properties.

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "[t0].[Quantity] = [t0].[Stock]")]
    [InlineData(ComparisonOperator.NotEqual, false, "[t0].[Quantity] <> [t0].[Stock]")]
    [InlineData(ComparisonOperator.LessThan, false, "[t0].[Quantity] < [t0].[Stock]")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, true, "[t0].[Quantity] < [t0].[Stock]")]
    [InlineData(ComparisonOperator.Equal, true, "[t0].[Quantity] <> [t0].[Stock]")]
    public void NonNullableProperties_NeedNoNullHandling(ComparisonOperator comparison, bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(Compare(Quantity, comparison, Int(Stock)), negated)));

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "[t0].[Quantity] = [t0].[Discount]")]
    [InlineData(ComparisonOperator.NotEqual, false, "([t0].[Quantity] <> [t0].[Discount] OR [t0].[Discount] IS NULL)")]
    [InlineData(ComparisonOperator.GreaterThan, false, "[t0].[Quantity] > [t0].[Discount]")]
    [InlineData(ComparisonOperator.Equal, true, "([t0].[Quantity] <> [t0].[Discount] OR [t0].[Discount] IS NULL)")]
    [InlineData(ComparisonOperator.NotEqual, true, "[t0].[Quantity] = [t0].[Discount]")]
    [InlineData(ComparisonOperator.GreaterThan, true, "([t0].[Quantity] <= [t0].[Discount] OR [t0].[Discount] IS NULL)")]
    public void OneNullableProperty_MatchesItsNullsWhereCSharpDoes(ComparisonOperator comparison, bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(Compare(Quantity, comparison, Discount), negated)));

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, "([t0].[Code] = [t0].[Note] OR ([t0].[Code] IS NULL AND [t0].[Note] IS NULL))")]
    [InlineData(
        ComparisonOperator.NotEqual,
        false,
        "(([t0].[Code] <> [t0].[Note] OR [t0].[Code] IS NULL OR [t0].[Note] IS NULL) AND ([t0].[Code] IS NOT NULL OR [t0].[Note] IS NOT NULL))")]
    [InlineData(
        ComparisonOperator.Equal,
        true,
        "(([t0].[Code] <> [t0].[Note] OR [t0].[Code] IS NULL OR [t0].[Note] IS NULL) AND ([t0].[Code] IS NOT NULL OR [t0].[Note] IS NOT NULL))")]
    [InlineData(ComparisonOperator.NotEqual, true, "([t0].[Code] = [t0].[Note] OR ([t0].[Code] IS NULL AND [t0].[Note] IS NULL))")]
    public void TwoNullableProperties_AreEqualWhenBothAreNull(ComparisonOperator comparison, bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(Compare(Code, comparison, Note), negated)));

    [Theory]
    [InlineData(false, "[t0].[Price] > [t0].[Cost]")]
    [InlineData(true, "([t0].[Price] <= [t0].[Cost] OR [t0].[Cost] IS NULL)")]
    public void OrderingComparisonWithANull_IsFalse_SoItsNegationIsTrue(bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(Compare(Price, ComparisonOperator.GreaterThan, Cost), negated)));

    // --- Arithmetic.

    [Fact]
    public void Arithmetic_CastsWhereCSharpConverts_AsEfCoreDoes()
    {
        var statement = Build(Where(Compare(Bin(ValueOperator.Multiply, Price, Convert(Quantity, typeof(decimal))), ComparisonOperator.GreaterThan, S(0)), 100m));

        Assert.EndsWith(" WHERE [t0].[Price] * CAST([t0].[Quantity] AS decimal(18,2)) > @p0", statement.CommandText, StringComparison.Ordinal);
        Assert.Null(Assert.Single(statement.Parameters).Property);
    }

    [Fact]
    public void ValueCombinedWithAProperty_IsTypedByItsColumn()
    {
        var statement = Build(Where(Compare(Bin(ValueOperator.Subtract, Quantity, S(0)), ComparisonOperator.GreaterThan, S(1)), 5, 0));

        Assert.EndsWith(" WHERE [t0].[Quantity] - @p0 > @p1", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal(nameof(Gadget.Quantity), statement.Parameters[0].Property!.Name);
        Assert.Null(statement.Parameters[1].Property);
    }

    [Fact]
    public void NestedOperators_AreParenthesized()
        => Assert.Equal(
            "([t0].[Quantity] + [t0].[Stock]) * @p0 > @p1",
            WhereSql(Compare(Bin(ValueOperator.Multiply, Bin(ValueOperator.Add, Quantity, Int(Stock)), S(0)), ComparisonOperator.GreaterThan, S(1)), 2, 20));

    [Theory]
    [InlineData(ValueOperator.Divide, "[t0].[Quantity] / @p0 = @p1")]
    [InlineData(ValueOperator.Modulo, "[t0].[Quantity] % @p0 = @p1")]
    [InlineData(ValueOperator.BitwiseAnd, "[t0].[Quantity] & @p0 = @p1")]
    [InlineData(ValueOperator.BitwiseOr, "[t0].[Quantity] | @p0 = @p1")]
    [InlineData(ValueOperator.ExclusiveOr, "[t0].[Quantity] ^ @p0 = @p1")]
    public void EachOperator_IsWrittenAsInSql(ValueOperator op, string expected)
        => Assert.Equal(expected, WhereSql(Compare(Bin(op, Quantity, S(0)), ComparisonOperator.Equal, S(1)), 2, 1));

    [Fact]
    public void UnaryOperators_ParenthesizeACompoundOperand()
    {
        Assert.Equal("-[t0].[Quantity] < @p0", WhereSql(Compare(new UnaryValueTemplate(UnaryValueOperator.Negate, Quantity), ComparisonOperator.LessThan, S(0)), -5));
        Assert.Equal(
            "~([t0].[Quantity] + @p0) = @p1",
            WhereSql(Compare(new UnaryValueTemplate(UnaryValueOperator.BitwiseNot, Bin(ValueOperator.Add, Quantity, S(0))), ComparisonOperator.Equal, S(1)), 1, -14));
    }

    [Fact]
    public void NullableOperand_MakesTheResultNullable()
        => Assert.Equal(
            "([t0].[Discount] + @p0 <> @p1 OR ([t0].[Discount] + @p0) IS NULL)",
            WhereSql(Compare(Bin(ValueOperator.Add, Discount, S(0)), ComparisonOperator.NotEqual, S(1)), 1, 3));

    [Fact]
    public void NullOperand_LiftsTheOperatorToNull()
    {
        // The value read when the query runs decides it, as C# lifts quantity + null to null.
        var template = Compare(Bin(ValueOperator.Add, Quantity, S(0)), ComparisonOperator.Equal, S(1));

        Assert.Equal("[t0].[Quantity] + @p0 = @p1", WhereSql(template, 1, 4));
        Assert.Equal("0 = 1", WhereSql(template, null, 4));
        Assert.Equal("1 = 1", WhereSql(template, null, null));
        Assert.Equal("([t0].[Discount] + @p0) IS NULL", WhereSql(Compare(Bin(ValueOperator.Add, Discount, S(0)), ComparisonOperator.Equal, S(1)), 1, null));
    }

    [Theory]
    [InlineData(nameof(Gadget.Quantity), typeof(long), "CAST([t0].[Quantity] AS bigint)")]
    [InlineData(nameof(Gadget.Quantity), typeof(double), "CAST([t0].[Quantity] AS float)")]
    [InlineData(nameof(Gadget.Price), typeof(int), "CAST([t0].[Price] AS int)")]
    [InlineData(nameof(Gadget.Rating), typeof(int), "CAST([t0].[Rating] AS int)")]
    [InlineData(nameof(Gadget.Quantity), typeof(short), "CAST([t0].[Quantity] AS smallint)")]
    [InlineData(nameof(Gadget.Stock), typeof(int), "[t0].[Stock]")]
    [InlineData(nameof(Gadget.Status), typeof(int), "[t0].[Status]")]
    [InlineData(nameof(Gadget.Quantity), typeof(GadgetStatus), "[t0].[Quantity]")]
    [InlineData(nameof(Gadget.Discount), typeof(int?), "[t0].[Discount]")]
    [InlineData(nameof(Gadget.Quantity), typeof(string), "CAST([t0].[Quantity] AS nvarchar(max))")]
    public void Conversions_CastOnlyWhereSqlNeedsIt(string property, Type type, string expected)
        => Assert.Equal(expected + " = @p0", WhereSql(Compare(Convert(P(property), type), ComparisonOperator.Equal, S(0)), 1));

    [Fact]
    public void ConversionToATypeWithoutStoreType_Throws()
        => Assert.Throws<NotSupportedException>(() => WhereSql(Compare(Convert(Quantity, typeof(Uri)), ComparisonOperator.Equal, S(0)), 1));

    // --- Strings.

    [Fact]
    public void Concatenation_ReadsANullStringAsEmpty_AsCSharpDoes()
    {
        Assert.Equal(
            "[t0].[Name] + COALESCE([t0].[Code], N'') = @p0",
            WhereSql(Compare(Bin(ValueOperator.Concat, Name, Code), ComparisonOperator.Equal, S(0)), "DuneDU"));
        Assert.Equal(
            "[t0].[Name] + N'' = @p0",
            WhereSql(Compare(Bin(ValueOperator.Concat, Name, S(0)), ComparisonOperator.Equal, S(1)), null, "Dune"));

        // Never null, so == null is false without asking the database.
        Assert.Equal("0 = 1", WhereSql(Compare(Bin(ValueOperator.Concat, Code, Note), ComparisonOperator.Equal, S(0)), [null]));
    }

    [Fact]
    public void ConcatenatedConcatenation_IsParenthesized()
        => Assert.Equal(
            "[t0].[Name] + (@p0 + CAST([t0].[Quantity] AS nvarchar(max))) = @p1",
            WhereSql(
                Compare(Bin(ValueOperator.Concat, Name, Bin(ValueOperator.Concat, S(0), Convert(Quantity, typeof(string)))), ComparisonOperator.Equal, S(1)),
                "-",
                "Dune-12"));

    // --- Coalescing and conditionals.

    [Fact]
    public void Coalesce_IsNullableOnlyWhenItsLastValueIs()
    {
        var statement = Build(Where(new NotTemplate(Compare(new CoalesceValueTemplate(Discount, S(0)), ComparisonOperator.GreaterThan, S(1))), 0, 0));

        Assert.EndsWith(" WHERE COALESCE([t0].[Discount], @p0) <= @p1", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal(nameof(Gadget.Discount), statement.Parameters[0].Property!.Name);
        Assert.Equal(
            "(COALESCE([t0].[Code], [t0].[Note]) <> @p0 OR COALESCE([t0].[Code], [t0].[Note]) IS NULL)",
            WhereSql(Compare(new CoalesceValueTemplate(Code, Note), ComparisonOperator.NotEqual, S(0)), "x"));
    }

    [Fact]
    public void Coalesce_OfAValueNeverNull_IsThatValue()
    {
        var statement = Build(Where(Compare(new CoalesceValueTemplate(Quantity, S(0)), ComparisonOperator.Equal, S(1)), 7, 3));

        Assert.EndsWith(" WHERE [t0].[Quantity] = @p0", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal(3, Assert.Single(statement.Parameters).Value);
        Assert.Equal("[t0].[Discount] = @p0", WhereSql(Compare(new CoalesceValueTemplate(S(0), Discount), ComparisonOperator.Equal, S(1)), null, 3));
    }

    [Fact]
    public void Conditional_IsACase_WithItsBranchesTypedByEachOther()
    {
        var statement = Build(Where(Compare(new ConditionalValueTemplate(new BooleanPropertyTemplate(nameof(Gadget.IsActive)), Price, S(0)), ComparisonOperator.GreaterThan, S(1)), 0m, 5m));

        Assert.EndsWith(
            " WHERE CASE WHEN [t0].[IsActive] = CAST(1 AS bit) THEN [t0].[Price] ELSE @p0 END > @p1", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal(nameof(Gadget.Price), statement.Parameters[0].Property!.Name);
    }

    [Fact]
    public void Conditional_WithANullableBranch_IsNullable()
        => Assert.Equal(
            "(CASE WHEN [t0].[Discount] IS NULL THEN @p0 ELSE [t0].[Discount] END <= @p1 OR " +
            "CASE WHEN [t0].[Discount] IS NULL THEN @p0 ELSE [t0].[Discount] END IS NULL)",
            WhereSql(
                new NotTemplate(Compare(
                    new ConditionalValueTemplate(new NullCheckTemplate(nameof(Gadget.Discount), isNull: true), S(0), Discount),
                    ComparisonOperator.GreaterThan,
                    S(1))),
                0,
                0));

    [Fact]
    public void ConditionUsedAsAValue_IsACaseThatIsNeverNull()
        => Assert.Equal(
            "CASE WHEN [t0].[Quantity] > @p0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = [t0].[IsActive]",
            WhereSql(Compare(Condition(new ComparisonTemplate(nameof(Gadget.Quantity), ComparisonOperator.GreaterThan, 0)), ComparisonOperator.Equal, IsActive), 5));

    // --- Booleans.

    [Theory]
    [InlineData(false, "COALESCE([t0].[IsFeatured], @p0) = CAST(1 AS bit)")]
    [InlineData(true, "COALESCE([t0].[IsFeatured], @p0) = CAST(0 AS bit)")]
    public void BooleanValue_NeverNull_IsComparedWithTrue(bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(new BooleanValueTemplate(new CoalesceValueTemplate(IsFeatured, S(0))), negated), false));

    [Theory]
    [InlineData(false, "[t0].[IsFeatured] = CAST(1 AS bit)")]
    [InlineData(true, "([t0].[IsFeatured] = CAST(0 AS bit) OR [t0].[IsFeatured] IS NULL)")]
    public void BooleanValue_Nullable_IsFalseWhenNull(bool negated, string expected)
        => Assert.Equal(expected, WhereSql(Negate(new BooleanValueTemplate(IsFeatured), negated)));

    [Fact]
    public void BooleanExclusiveOr_ComparesTheBits()
        => Assert.Equal(
            "[t0].[IsActive] ^ CASE WHEN [t0].[Quantity] > @p0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = CAST(1 AS bit)",
            WhereSql(
                new BooleanValueTemplate(Bin(ValueOperator.ExclusiveOr, IsActive, Condition(new ComparisonTemplate(nameof(Gadget.Quantity), ComparisonOperator.GreaterThan, 0)))),
                5));

    [Fact]
    public void BooleanValueOfNull_IsFalse()
    {
        Assert.Equal("0 = 1", WhereSql(new BooleanValueTemplate(S(0)), [null]));
        Assert.Equal("1 = 1", WhereSql(new NotTemplate(new BooleanValueTemplate(S(0))), [null]));
    }

    // --- Value converters.

    [Fact]
    public void ValueComparedWithAConvertedProperty_IsSentAsTheColumnHoldsIt()
    {
        var statement = Build(Where(Compare(Grade, ComparisonOperator.Equal, S(0)), GadgetGrade.B));

        Assert.EndsWith(" WHERE [t0].[Grade] = @p0", statement.CommandText, StringComparison.Ordinal);
        Assert.Equal("B", Assert.Single(statement.Parameters).Value);
    }

    [Fact]
    public void ValueCombinedWithAConvertedProperty_IsNotConverted()
    {
        var statement = Build(Where(Compare(Bin(ValueOperator.Concat, Grade, S(0)), ComparisonOperator.Equal, S(1)), "!", "B!"));

        Assert.All(statement.Parameters, p => Assert.Null(p.Property));
    }

    // --- Navigations.

    [Fact]
    public void PropertiesThroughNavigations_AreJoined()
    {
        Assert.Equal(
            "SELECT " + Cols("t0") + From + " INNER JOIN [Maker] AS [t1] ON [t0].[MakerId] = [t1].[Id] WHERE [t1].[Name] + [t0].[Name] = @p0",
            Build(Where(Compare(Bin(ValueOperator.Concat, P("Maker.Name"), Name), ComparisonOperator.Equal, S(0)), "AcmeDune")).CommandText);

        // A missing optional reference reads null.
        Assert.EndsWith(
            " LEFT JOIN [Shop] AS [t1] ON [t0].[ShopId] = [t1].[Id] WHERE ([t1].[Name] <> [t0].[Name] OR [t1].[Name] IS NULL)",
            Build(Where(Compare(P("Shop.Name"), ComparisonOperator.NotEqual, Name))).CommandText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationNullCheckInsideAValue_ChecksTheForeignKey()
        => Assert.Equal(
            "CASE WHEN [t0].[ShopId] IS NULL THEN @p0 ELSE @p1 END = @p2",
            WhereSql(Compare(new ConditionalValueTemplate(new NullCheckTemplate("Shop", isNull: true), S(0), S(1)), ComparisonOperator.Equal, S(2)), 0, 1, 1));

    // --- Order keys.

    [Fact]
    public void ComputedOrderKey_IsWrittenInOrderBy()
    {
        var query = SqlQuery.ThenBy(
            SqlQuery.OrderByDescending(Query(), Bin(ValueOperator.Multiply, Price, Convert(Quantity, typeof(decimal))), []),
            new CoalesceValueTemplate(Code, Name),
            []);

        Assert.Equal(
            "SELECT " + Cols("t0") + From + " ORDER BY [t0].[Price] * CAST([t0].[Quantity] AS decimal(18,2)) DESC, COALESCE([t0].[Code], [t0].[Name])",
            Build(query).CommandText);
    }

    [Fact]
    public void OrderKeyThatDoesNotReadTheRow_OrdersNothing_AsInEfCore()
    {
        var byNothing = SqlQuery.OrderBy(SqlQuery.OrderBy(Query(), nameof(Gadget.Name)), S(0), [5]);

        Assert.Equal("SELECT " + Cols("t0") + From, Build(byNothing).CommandText);
        Assert.Equal("SELECT " + Cols("t0") + From + " ORDER BY [t0].[Id]", Build(SqlQuery.ThenBy(byNothing, nameof(Gadget.Id))).CommandText);
    }

    [Fact]
    public void ComputedOrderKey_IsSelectedByADerivedTable()
    {
        var query = SqlQuery.Where(
            SqlQuery.Take(SqlQuery.OrderBy(Query(), Condition(new ComparisonTemplate(nameof(Gadget.Quantity), ComparisonOperator.GreaterThan, 0)), [5]), 3),
            new ComparisonTemplate(nameof(Gadget.Id), ComparisonOperator.NotEqual, 0),
            [1]);

        Assert.Equal(
            "SELECT " + Cols("t1") + " FROM (SELECT TOP(@p1) " + Cols("t0") +
            ", CASE WHEN [t0].[Quantity] > @p0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [c]" + From +
            " ORDER BY CASE WHEN [t0].[Quantity] > @p0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END) AS [t1] WHERE [t1].[Id] <> @p2 ORDER BY [t1].[c]",
            Build(query).CommandText);
    }

    [Fact]
    public void OrderKeyThroughANavigation_IsJoined()
        => Assert.Equal(
            "SELECT " + Cols("t0") + From + " INNER JOIN [Maker] AS [t1] ON [t0].[MakerId] = [t1].[Id] ORDER BY [t1].[Name] + [t0].[Name]",
            Build(SqlQuery.OrderBy(Query(), Bin(ValueOperator.Concat, P("Maker.Name"), Name), [])).CommandText);

    [Fact]
    public void OrderKeyWithTheWrongNumberOfValues_Throws()
        => Assert.Throws<ArgumentException>(() => SqlQuery.OrderBy(Query(), Bin(ValueOperator.Add, Quantity, S(1)), [1]));

    // --- Templates.

    [Fact]
    public void Templates_CountTheSlotsTheyRead()
    {
        Assert.Equal(3, Bin(ValueOperator.Add, S(2), Quantity).ValueCount);
        Assert.Equal(4, new ConditionalValueTemplate(new ComparisonTemplate("Id", ComparisonOperator.Equal, 3), S(0), Quantity).ValueCount);
        Assert.Equal(2, Compare(Quantity, ComparisonOperator.Equal, new CoalesceValueTemplate(Discount, S(1))).ValueCount);
        Assert.Equal(0, new BooleanValueTemplate(IsActive).ValueCount);
        Assert.Equal(typeof(int), Convert(Discount, typeof(int?)).Type);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotValueTemplate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bin((ValueOperator)99, Quantity, Quantity));
    }

    [Fact]
    public void SameTemplate_BindsOncePerEntity()
    {
        var entity = BindType(typeof(Gadget));
        var template = Bin(ValueOperator.Add, Quantity, S(0));

        Assert.Same(entity.BindValue(template), entity.BindValue(template));
    }

    private static PropertyValueTemplate P(string name) => new(name);

    private static SlotValueTemplate S(int index) => new(index);

    private static BinaryValueTemplate Bin(ValueOperator op, ValueTemplate left, ValueTemplate right) => new(op, left, right);

    private static ConvertValueTemplate Convert(ValueTemplate operand, Type type) => new(operand, type);

    // C# widens a short to int before comparing it with an int.
    private static ConvertValueTemplate Int(ValueTemplate operand) => new(operand, typeof(int));

    private static PredicateValueTemplate Condition(PredicateTemplate predicate) => new(predicate);

    private static ValueComparisonTemplate Compare(ValueTemplate left, ComparisonOperator comparison, ValueTemplate right) => new(left, comparison, right);

    private static PredicateTemplate Negate(PredicateTemplate predicate, bool negated) => negated ? new NotTemplate(predicate) : predicate;

    private IQueryable<Gadget> Query() => SqlQuery.ToSqlQueryable(_db.Gadgets);

    private IQueryable<Gadget> Where(PredicateTemplate predicate, params object?[] values) => SqlQuery.Where(Query(), predicate, values);

    // The WHERE clause the predicate builds.
    private string WhereSql(PredicateTemplate predicate, params object?[] values)
    {
        var text = Build(Where(predicate, values)).CommandText;
        var where = text.IndexOf(" WHERE ", StringComparison.Ordinal);
        return text[(where + " WHERE ".Length)..];
    }

    private SqlStatement Build(IQueryable<Gadget> query)
        => SqlBuilder.Build(
            BindType(typeof(Gadget)), ((SqlQueryable<Gadget>)query).Tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: null, [],
            aggregate: null, static _ => [], BindType);

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);

    private string Cols(string alias) => string.Join(", ", BindType(typeof(Gadget)).Properties.Select(p => $"[{alias}].[{p.ColumnName}]"));
}
