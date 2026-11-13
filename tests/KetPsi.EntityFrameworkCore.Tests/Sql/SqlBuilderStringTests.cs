using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>String methods and <c>EF.Functions.Like</c>, written as EF Core writes them on SQL Server (probed on EF Core 9, 10 and 11).</summary>
public sealed class SqlBuilderStringTests : IDisposable
{
    private static readonly ValueTemplate Name = new PropertyValueTemplate(nameof(Gadget.Name));
    private static readonly ValueTemplate Code = new PropertyValueTemplate(nameof(Gadget.Code));
    private static readonly ValueTemplate Note = new PropertyValueTemplate(nameof(Gadget.Note));
    private static readonly ValueTemplate Discount = new PropertyValueTemplate(nameof(Gadget.Discount));
    private static readonly ValueTemplate Quantity = new PropertyValueTemplate(nameof(Gadget.Quantity));

    private readonly GadgetContext _db = new();

    public void Dispose() => _db.Dispose();

    // --- Contains, StartsWith and EndsWith.

    [Theory]
    [InlineData(StringMatch.Contains, "o", "%o%")]
    [InlineData(StringMatch.StartsWith, "D", "D%")]
    [InlineData(StringMatch.EndsWith, "e", "%e")]
    [InlineData(StringMatch.Contains, "50%_[x]\\", "%50\\%\\_\\[x]\\\\%")]
    [InlineData(StringMatch.Contains, "", "%%")]
    public void MatchWithAValue_IsALikeWithItsWildcardsEscaped(StringMatch match, string part, string pattern)
    {
        var statement = Build(Where(new StringMatchTemplate(match, Name, S(0)), part));

        Assert.EndsWith(" WHERE [t0].[Name] LIKE @p0 ESCAPE N'\\'", statement.CommandText, StringComparison.Ordinal);
        var parameter = Assert.Single(statement.Parameters);
        Assert.Equal(pattern, parameter.Value);
        Assert.Null(parameter.Property);
    }

    [Fact]
    public void MatchWithACharacter_SendsItAsAString()
        => Assert.Equal("%o%", Assert.Single(Build(Where(new StringMatchTemplate(StringMatch.Contains, Name, S(0)), 'o')).Parameters).Value);

    [Fact]
    public void NegatedMatch_KeepsTheNullsCSharpDoes()
    {
        Assert.Equal("NOT ([t0].[Name] LIKE @p0 ESCAPE N'\\')", WhereSql(new NotTemplate(new StringMatchTemplate(StringMatch.Contains, Name, S(0))), "o"));
        Assert.Equal(
            "(NOT ([t0].[Note] LIKE @p0 ESCAPE N'\\') OR [t0].[Note] IS NULL)",
            WhereSql(new NotTemplate(new StringMatchTemplate(StringMatch.StartsWith, Note, S(0))), "D"));
    }

    [Fact]
    public void MatchWithNull_IsFalse_AsInEfCore()
    {
        Assert.Equal("0 = 1", WhereSql(new StringMatchTemplate(StringMatch.Contains, Name, S(0)), [null]));
        Assert.Equal("1 = 1", WhereSql(new NotTemplate(new StringMatchTemplate(StringMatch.Contains, Name, S(0))), [null]));
    }

    [Fact]
    public void MatchWithAColumn_SearchesForItsValue()
    {
        Assert.Equal(
            "([t0].[Code] IS NOT NULL AND (CHARINDEX([t0].[Code], [t0].[Name]) > 0 OR [t0].[Code] LIKE N''))",
            WhereSql(new StringMatchTemplate(StringMatch.Contains, Name, Code)));
        Assert.Equal(
            "([t0].[Code] IS NOT NULL AND LEFT([t0].[Name], CAST(LEN([t0].[Code]) AS int)) = [t0].[Code])",
            WhereSql(new StringMatchTemplate(StringMatch.StartsWith, Name, Code)));
        Assert.Equal(
            "([t0].[Note] IS NOT NULL AND [t0].[Code] IS NOT NULL AND RIGHT([t0].[Note], CAST(LEN([t0].[Code]) AS int)) = [t0].[Code])",
            WhereSql(new StringMatchTemplate(StringMatch.EndsWith, Note, Code)));
    }

    [Fact]
    public void NegatedMatchWithAColumn_IsItsExactNegation()
    {
        Assert.Equal(
            "NOT ([t0].[Code] IS NOT NULL AND (CHARINDEX([t0].[Code], [t0].[Name]) > 0 OR [t0].[Code] LIKE N''))",
            WhereSql(new NotTemplate(new StringMatchTemplate(StringMatch.Contains, Name, Code))));
        Assert.Equal(
            "NOT (LEFT([t0].[Name], CAST(LEN([t0].[Name]) AS int)) = [t0].[Name])",
            WhereSql(new NotTemplate(new StringMatchTemplate(StringMatch.StartsWith, Name, Name))));
    }

    // --- EF.Functions.Like.

    [Fact]
    public void Like_IsSentAsWritten()
    {
        Assert.Equal("[t0].[Name] LIKE @p0", WhereSql(new LikeTemplate(Name, S(0)), "D%"));
        Assert.Equal("[t0].[Note] LIKE @p0 ESCAPE @p1", WhereSql(new LikeTemplate(Note, S(0), S(1)), "%!%%", "!"));
        Assert.Equal("[t0].[Name] LIKE [t0].[Code]", WhereSql(new LikeTemplate(Name, Code)));
        Assert.Equal("0 = 1", WhereSql(new LikeTemplate(Name, S(0)), [null]));
    }

    [Fact]
    public void NegatedLike_KeepsTheNulls()
    {
        Assert.Equal("(NOT ([t0].[Note] LIKE @p0) OR [t0].[Note] IS NULL)", WhereSql(new NotTemplate(new LikeTemplate(Note, S(0))), "D%"));
        Assert.Equal("(NOT ([t0].[Name] LIKE [t0].[Code]) OR [t0].[Code] IS NULL)", WhereSql(new NotTemplate(new LikeTemplate(Name, Code))));
        Assert.Equal("NOT ([t0].[Name] LIKE @p0)", WhereSql(new NotTemplate(new LikeTemplate(Name, S(0))), "D%"));
    }

    // --- IsNullOrEmpty and IsNullOrWhiteSpace.

    [Theory]
    [InlineData(false, false, "([t0].[Note] IS NULL OR [t0].[Note] LIKE N'')")]
    [InlineData(false, true, "([t0].[Note] IS NOT NULL AND NOT ([t0].[Note] LIKE N''))")]
    [InlineData(true, false, "([t0].[Note] IS NULL OR [t0].[Note] = N'')")]
    [InlineData(true, true, "([t0].[Note] IS NOT NULL AND NOT ([t0].[Note] = N''))")]
    public void NullOrEmpty_IsWrittenAsInEfCore(bool orWhiteSpace, bool negated, string expected)
    {
        PredicateTemplate predicate = new NullOrEmptyTemplate(Note, orWhiteSpace);
        Assert.Equal(expected, WhereSql(negated ? new NotTemplate(predicate) : predicate));
    }

    [Fact]
    public void NullOrEmptyOfANonNullableString_OnlyTestsForEmpty()
        => Assert.Equal("[t0].[Name] LIKE N''", WhereSql(new NullOrEmptyTemplate(Name, orWhiteSpace: false)));

    // --- Functions.

    [Theory]
    [InlineData(ValueFunction.ToUpper, "UPPER([t0].[Name])")]
    [InlineData(ValueFunction.ToLower, "LOWER([t0].[Name])")]
    [InlineData(ValueFunction.Trim, "LTRIM(RTRIM([t0].[Name]))")]
    [InlineData(ValueFunction.TrimStart, "LTRIM([t0].[Name])")]
    [InlineData(ValueFunction.TrimEnd, "RTRIM([t0].[Name])")]
    [InlineData(ValueFunction.ToString, "[t0].[Name]")]
    public void StringFunction_IsWrittenAsInEfCore(ValueFunction function, string sql)
        => Assert.Equal(sql + " = @p0", WhereSql(Compare(new FunctionValueTemplate(function, Name), S(0)), "DUNE"));

    [Fact]
    public void Length_IsLenAsAnInt()
        => Assert.Equal(
            "CAST(LEN([t0].[Name]) AS int) > @p0",
            WhereSql(new ValueComparisonTemplate(new FunctionValueTemplate(ValueFunction.Length, Name), ComparisonOperator.GreaterThan, S(0)), 4));

    [Fact]
    public void FunctionOfANullableString_IsNullable()
        => Assert.Equal(
            "(UPPER([t0].[Code]) <> @p0 OR UPPER([t0].[Code]) IS NULL)",
            WhereSql(new ValueComparisonTemplate(new FunctionValueTemplate(ValueFunction.ToUpper, Code), ComparisonOperator.NotEqual, S(0)), "DU"));

    [Fact]
    public void Substring_CountsFromOne()
    {
        Assert.Equal(
            "SUBSTRING([t0].[Name], @p0 + 1, @p1) = @p2",
            WhereSql(Compare(new FunctionValueTemplate(ValueFunction.Substring, Name, S(0), S(1)), S(2)), 0, 2, "Du"));
        Assert.Equal(
            "SUBSTRING([t0].[Name], ([t0].[Quantity] - @p0) + 1, CAST(LEN([t0].[Name]) AS int)) = @p1",
            WhereSql(Compare(new FunctionValueTemplate(ValueFunction.Substring, Name, new BinaryValueTemplate(ValueOperator.Subtract, Quantity, S(0))), S(1)), 1, "une"));
    }

    [Fact]
    public void Replace_IsReplace()
        => Assert.Equal(
            "REPLACE([t0].[Name], @p0, @p1) = @p2",
            WhereSql(Compare(new FunctionValueTemplate(ValueFunction.Replace, Name, S(0), S(1)), S(2)), "o", "0", "Z0rk"));

    [Fact]
    public void IndexOf_CountsFromZero_AndFindsTheEmptyStringAtZero()
    {
        var indexOf = new ValueComparisonTemplate(new FunctionValueTemplate(ValueFunction.IndexOf, Name, S(0)), ComparisonOperator.Equal, S(1));

        Assert.Equal("CHARINDEX(@p0, [t0].[Name]) - 1 = @p1", WhereSql(indexOf, "o", 1));
        Assert.Equal("CHARINDEX(@p0, [t0].[Name]) - 0 = @p1", WhereSql(indexOf, "", 0));
        Assert.Equal("CHARINDEX(@p0, [t0].[Name]) - 1 = @p1", WhereSql(indexOf, 'o', 1));
        Assert.Equal(
            "CHARINDEX([t0].[Code], [t0].[Name]) - CASE WHEN [t0].[Code] = N'' THEN 0 ELSE 1 END = @p0",
            WhereSql(new ValueComparisonTemplate(new FunctionValueTemplate(ValueFunction.IndexOf, Name, Code), ComparisonOperator.Equal, S(0)), 0));
    }

    [Fact]
    public void ToStringOfANumber_CastsIt_AndANullNumberIsEmpty()
    {
        Assert.Equal(
            "CAST([t0].[Quantity] AS nvarchar(max)) = @p0",
            WhereSql(Compare(new FunctionValueTemplate(ValueFunction.ToString, Quantity), S(0)), "12"));
        Assert.Equal(
            "COALESCE(CAST([t0].[Discount] AS nvarchar(max)), N'') = @p0",
            WhereSql(Compare(new FunctionValueTemplate(ValueFunction.ToString, Discount), S(0)), "2"));
    }

    [Fact]
    public void ToStringOfAnotherType_Throws()
        => Assert.Throws<NotSupportedException>(() => WhereSql(
            Compare(new FunctionValueTemplate(ValueFunction.ToString, new PropertyValueTemplate(nameof(Gadget.Rating))), S(0)), "1"));

    [Fact]
    public void FunctionOfNull_IsNull()
        => Assert.Equal("0 = 1", WhereSql(Compare(new FunctionValueTemplate(ValueFunction.ToUpper, S(0)), S(1)), null, "X"));

    [Fact]
    public void FunctionOrderKey_IsWrittenInOrderBy()
        => Assert.EndsWith(
            " ORDER BY CAST(LEN([t0].[Name]) AS int) DESC",
            Build(SqlQuery.OrderByDescending(Query(), new FunctionValueTemplate(ValueFunction.Length, Name), [])).CommandText,
            StringComparison.Ordinal);

    [Fact]
    public void StringPredicatesThroughNavigations_AreJoined()
        => Assert.EndsWith(
            " INNER JOIN [Maker] AS [t1] ON [t0].[MakerId] = [t1].[Id] WHERE [t1].[Name] LIKE @p0 ESCAPE N'\\'",
            Build(Where(new StringMatchTemplate(StringMatch.StartsWith, new PropertyValueTemplate("Maker.Name"), S(0)), "A")).CommandText,
            StringComparison.Ordinal);

    [Fact]
    public void Templates_CheckTheirArguments()
    {
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.Substring, Name));
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.ToUpper, Name, Name));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringMatchTemplate((StringMatch)9, Name, Name));
        Assert.Equal(3, new LikeTemplate(Name, S(0), S(2)).ValueCount);
        Assert.Equal(2, new FunctionValueTemplate(ValueFunction.Replace, Name, S(1), Name).ValueCount);
    }

    private static SlotValueTemplate S(int index) => new(index);

    private static ValueComparisonTemplate Compare(ValueTemplate left, ValueTemplate right) => new(left, ComparisonOperator.Equal, right);

    private IQueryable<Gadget> Query() => SqlQuery.ToSqlQueryable(_db.Gadgets);

    private IQueryable<Gadget> Where(PredicateTemplate predicate, params object?[] values) => SqlQuery.Where(Query(), predicate, values);

    private string WhereSql(PredicateTemplate predicate, params object?[] values)
    {
        var text = Build(Where(predicate, values)).CommandText;
        return text[(text.IndexOf(" WHERE ", StringComparison.Ordinal) + " WHERE ".Length)..];
    }

    private SqlStatement Build(IQueryable<Gadget> query)
        => SqlBuilder.Build(
            BindType(typeof(Gadget)), ((SqlQueryable<Gadget>)query).Tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: null, [],
            aggregate: null, static _ => [], BindType);

    private BoundEntity BindType(Type type) => ModelBinder.GetEntity(_db.Model, type);
}
