using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

/// <summary>Date and math functions, written as EF Core writes them on SQL Server (probed on EF Core 9, 10 and 11).</summary>
public sealed class SqlBuilderDateMathTests : IDisposable
{
    private static readonly ValueTemplate Created = new PropertyValueTemplate(nameof(Gadget.CreatedAt));
    private static readonly ValueTemplate Shipped = new PropertyValueTemplate(nameof(Gadget.ShippedAt));
    private static readonly ValueTemplate Day = new PropertyValueTemplate(nameof(Gadget.Day));
    private static readonly ValueTemplate Price = new PropertyValueTemplate(nameof(Gadget.Price));
    private static readonly ValueTemplate Quantity = new PropertyValueTemplate(nameof(Gadget.Quantity));
    private static readonly ValueTemplate Views = new PropertyValueTemplate(nameof(Gadget.Views));
    private static readonly ValueTemplate Rating = new PropertyValueTemplate(nameof(Gadget.Rating));
    private static readonly ValueTemplate Cost = new PropertyValueTemplate(nameof(Gadget.Cost));

    private readonly GadgetContext _db = new();

    public void Dispose() => _db.Dispose();

    // --- Dates.

    [Theory]
    [InlineData(ValueFunction.Year, "DATEPART(year, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Month, "DATEPART(month, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Day, "DATEPART(day, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Hour, "DATEPART(hour, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Minute, "DATEPART(minute, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Second, "DATEPART(second, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.Millisecond, "DATEPART(millisecond, [t0].[CreatedAt])")]
    [InlineData(ValueFunction.DayOfYear, "DATEPART(dayofyear, [t0].[CreatedAt])")]
    public void DatePart_IsADatePart(ValueFunction part, string sql)
    {
        var statement = Build(Where(Compare(F(part, Created), ComparisonOperator.Equal, S(0)), 3));

        Assert.EndsWith(" WHERE " + sql + " = @p0", statement.CommandText, StringComparison.Ordinal);
        Assert.Null(Assert.Single(statement.Parameters).Property);
    }

    [Fact]
    public void PartOfANullableDate_IsNullWithoutOne()
        => Assert.Equal("DATEPART(year, [t0].[ShippedAt]) = @p0", WhereSql(Compare(F(ValueFunction.Year, Shipped), ComparisonOperator.Equal, S(0)), 2024));

    [Fact]
    public void DateAndTimeOfDay_AreConversions()
    {
        Assert.Equal("CONVERT(date, [t0].[CreatedAt]) = @p0", WhereSql(Compare(F(ValueFunction.Date, Created), ComparisonOperator.Equal, S(0)), new DateTime(2024, 3, 10)));
        Assert.Equal("CONVERT(time, [t0].[CreatedAt]) > @p0", WhereSql(Compare(F(ValueFunction.TimeOfDay, Created), ComparisonOperator.GreaterThan, S(0)), TimeSpan.FromHours(6)));
        Assert.Equal("CAST([t0].[CreatedAt] AS date) = [t0].[Day]", WhereSql(Compare(F(ValueFunction.DateFromDateTime, Created), ComparisonOperator.Equal, Day)));
        Assert.Equal("DATEDIFF(day, '0001-01-01', [t0].[Day]) > @p0", WhereSql(Compare(F(ValueFunction.DayNumber, Day), ComparisonOperator.GreaterThan, S(0)), 738000));
    }

    [Theory]
    [InlineData(ValueFunction.AddYears, "year")]
    [InlineData(ValueFunction.AddMonths, "month")]
    [InlineData(ValueFunction.AddDays, "day")]
    [InlineData(ValueFunction.AddHours, "hour")]
    [InlineData(ValueFunction.AddMinutes, "minute")]
    [InlineData(ValueFunction.AddSeconds, "second")]
    [InlineData(ValueFunction.AddMilliseconds, "millisecond")]
    public void Add_IsADateAddOfAWholeNumber(ValueFunction add, string part)
        => Assert.Equal(
            $"DATEADD({part}, CAST(@p0 AS int), [t0].[CreatedAt]) > @p1",
            WhereSql(Compare(F(add, Created, S(0)), ComparisonOperator.GreaterThan, S(1)), 1.5, new DateTime(2024, 3, 1)));

    [Fact]
    public void AddOfAColumn_AndOfADateOnly_AreDateAdds()
    {
        Assert.Equal(
            "DATEADD(day, CAST([t0].[Quantity] AS int), [t0].[CreatedAt]) > [t0].[CreatedAt]",
            WhereSql(Compare(F(ValueFunction.AddDays, Created, Quantity), ComparisonOperator.GreaterThan, Created)));
        Assert.Equal(
            "DATEPART(month, DATEADD(day, CAST(@p0 AS int), [t0].[Day])) = @p1",
            WhereSql(Compare(F(ValueFunction.Month, F(ValueFunction.AddDays, Day, S(0))), ComparisonOperator.Equal, S(1)), 1, 3));
    }

    [Theory]
    [InlineData(ValueFunction.DateDiffYear, "year")]
    [InlineData(ValueFunction.DateDiffMonth, "month")]
    [InlineData(ValueFunction.DateDiffWeek, "week")]
    [InlineData(ValueFunction.DateDiffDay, "day")]
    [InlineData(ValueFunction.DateDiffHour, "hour")]
    [InlineData(ValueFunction.DateDiffMinute, "minute")]
    [InlineData(ValueFunction.DateDiffSecond, "second")]
    [InlineData(ValueFunction.DateDiffMillisecond, "millisecond")]
    public void DateDiff_CountsTheBoundaries(ValueFunction diff, string part)
        => Assert.Equal(
            $"DATEDIFF({part}, [t0].[CreatedAt], [t0].[ShippedAt]) > @p0",
            WhereSql(Compare(F(diff, Created, Shipped), ComparisonOperator.GreaterThan, S(0)), 1));

    [Fact]
    public void Clock_IsTheDatabasesClock()
    {
        Assert.Equal("[t0].[CreatedAt] < GETDATE()", WhereSql(Compare(Created, ComparisonOperator.LessThan, F(ValueFunction.Now))));
        Assert.Equal("[t0].[CreatedAt] < GETUTCDATE()", WhereSql(Compare(Created, ComparisonOperator.LessThan, F(ValueFunction.UtcNow))));
        Assert.Equal("[t0].[CreatedAt] < CONVERT(date, GETDATE())", WhereSql(Compare(Created, ComparisonOperator.LessThan, F(ValueFunction.Today))));
    }

    [Fact]
    public void DatePart_IsAnOrderKey()
    {
        var query = SqlQuery.OrderBy(Query(), F(ValueFunction.Year, Created), []);

        Assert.EndsWith(" ORDER BY DATEPART(year, [t0].[CreatedAt])", Build(query).CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void FunctionOfNull_IsNull()
        => Assert.Equal("0 = 1", WhereSql(Compare(F(ValueFunction.Year, S(0)), ComparisonOperator.Equal, S(1)), null, 2024));

    // --- Math.

    [Fact]
    public void Abs_KeepsTheType()
    {
        Assert.Equal("ABS([t0].[Quantity]) > @p0", WhereSql(Compare(F(ValueFunction.Abs, Quantity), ComparisonOperator.GreaterThan, S(0)), 2));
        Assert.Equal("ABS([t0].[Price]) > @p0", WhereSql(Compare(F(ValueFunction.Abs, Price), ComparisonOperator.GreaterThan, S(0)), 2m));
    }

    [Fact]
    public void Round_RoundsToTheDecimals()
    {
        Assert.Equal("ROUND([t0].[Price], 0) = @p0", WhereSql(Compare(F(ValueFunction.Round, Price), ComparisonOperator.Equal, S(0)), 3m));
        Assert.Equal("ROUND([t0].[Price], @p0) = @p1", WhereSql(Compare(F(ValueFunction.Round, Price, S(0)), ComparisonOperator.Equal, S(1)), 1, 2.5m));
        Assert.Equal("ROUND([t0].[Rating], 0, 1) = @p0", WhereSql(Compare(F(ValueFunction.Truncate, Rating), ComparisonOperator.Equal, S(0)), 2.0));
    }

    [Fact]
    public void FloorAndCeiling_KeepTheType()
    {
        Assert.Equal("FLOOR([t0].[Price]) = @p0", WhereSql(Compare(F(ValueFunction.Floor, Price), ComparisonOperator.Equal, S(0)), 2m));
        Assert.Equal("CEILING([t0].[Rating]) = @p0", WhereSql(Compare(F(ValueFunction.Ceiling, Rating), ComparisonOperator.Equal, S(0)), 3.0));
    }

    [Theory]
    [InlineData(ValueFunction.Sqrt, "SQRT")]
    [InlineData(ValueFunction.Log, "LOG")]
    [InlineData(ValueFunction.Log10, "LOG10")]
    [InlineData(ValueFunction.Exp, "EXP")]
    [InlineData(ValueFunction.Sin, "SIN")]
    [InlineData(ValueFunction.Cos, "COS")]
    [InlineData(ValueFunction.Tan, "TAN")]
    [InlineData(ValueFunction.Asin, "ASIN")]
    [InlineData(ValueFunction.Acos, "ACOS")]
    [InlineData(ValueFunction.Atan, "ATAN")]
    public void FunctionOfDouble_ConvertsAnIntegerToFloat(ValueFunction function, string name)
    {
        Assert.Equal($"{name}([t0].[Rating]) > @p0", WhereSql(Compare(F(function, Rating), ComparisonOperator.GreaterThan, S(0)), 1.0));
        Assert.Equal($"{name}(CAST([t0].[Views] AS float)) > @p0", WhereSql(Compare(F(function, Views), ComparisonOperator.GreaterThan, S(0)), 1.0));
    }

    [Fact]
    public void PowAndAtan2_TakeTwoDoubles()
    {
        Assert.Equal("POWER([t0].[Rating], @p0) > @p1", WhereSql(Compare(F(ValueFunction.Pow, Rating, S(0)), ComparisonOperator.GreaterThan, S(1)), 2.0, 4.0));
        Assert.Equal(
            "ATN2([t0].[Rating], CAST([t0].[Quantity] AS float)) > @p0",
            WhereSql(Compare(F(ValueFunction.Atan2, Rating, Quantity), ComparisonOperator.GreaterThan, S(0)), 1.0));
    }

    [Fact]
    public void Sign_IsAnInt()
    {
        Assert.Equal("SIGN([t0].[Quantity]) = @p0", WhereSql(Compare(F(ValueFunction.Sign, Quantity), ComparisonOperator.Equal, S(0)), -1));
        Assert.Equal("CAST(SIGN([t0].[Price]) AS int) = @p0", WhereSql(Compare(F(ValueFunction.Sign, Price), ComparisonOperator.Equal, S(0)), 1));
    }

    [Fact]
    public void FunctionOfANullableColumn_ComparesItsNulls()
        => Assert.Equal(
            "(ABS([t0].[Cost]) <> @p0 OR ABS([t0].[Cost]) IS NULL)",
            WhereSql(Compare(F(ValueFunction.Abs, Cost), ComparisonOperator.NotEqual, S(0)), 2m));

    [Fact]
    public void FunctionInAProjection_IsASelectedValue()
    {
        var tail = ((ProjectedQueryable<int>)SqlQuery.Select(
            Query(), [], [], [F(ValueFunction.Year, Created), F(ValueFunction.Round, Price, S(0))], [2], static (_, _, _) => 0)).Tail;

        Assert.Equal(
            "SELECT DATEPART(year, [t0].[CreatedAt]), ROUND([t0].[Price], @p0) FROM [Gadgets] AS [t0]",
            SqlBuilder.Build(BindType(typeof(Gadget)), tail, SqlServerDialect.Instance, QueryTerminal.Rows, rowLimit: null, [], aggregate: null, static _ => [], BindType).CommandText);
    }

    // --- Templates.

    [Fact]
    public void Template_ChecksTheArgumentCount()
    {
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.Now, Created));
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.AddDays, Created));
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.Round, Price, S(0), S(1)));
        Assert.Throws<ArgumentException>(() => new FunctionValueTemplate(ValueFunction.DateDiffDay, Created));
        Assert.Equal(0, new FunctionValueTemplate(ValueFunction.Today).ValueCount);
        Assert.Equal(3, new FunctionValueTemplate(ValueFunction.Pow, S(0), S(2)).ValueCount);
    }

    private static FunctionValueTemplate F(ValueFunction function, params ValueTemplate[] arguments) => new(function, arguments);

    private static SlotValueTemplate S(int index) => new(index);

    private static ValueComparisonTemplate Compare(ValueTemplate left, ComparisonOperator comparison, ValueTemplate right) => new(left, comparison, right);

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
