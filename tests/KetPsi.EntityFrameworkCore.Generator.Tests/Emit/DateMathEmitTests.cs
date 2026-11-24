using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

/// <summary>Date and math functions in predicates and order keys: their templates, and that they compile in the generated code.</summary>
public sealed class DateMathEmitTests
{
    private const string Templates = "global::KetPsi.EntityFrameworkCore.Infrastructure.Templates";

    private const string Model = """
        public sealed class Order
        {
            public int Id { get; set; }
            public decimal Price { get; set; }
            public decimal? Discount { get; set; }
            public int Quantity { get; set; }
            public long Views { get; set; }
            public double Rating { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? ShippedAt { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly At { get; set; }
            public DateTimeOffset Due { get; set; }
        }

        public sealed class OrderDb : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        """;

    [Theory]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.Year == 2024 && o.CreatedAt.Month >= 2 && o.CreatedAt.Day < 30).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.Hour + o.CreatedAt.Minute + o.CreatedAt.Second + o.CreatedAt.Millisecond > o.CreatedAt.DayOfYear).ToList();")]
    [InlineData("var day = new DateTime(2024, 3, 10); var r = db.Orders.Where(o => o.CreatedAt.Date == day && o.CreatedAt.TimeOfDay > TimeSpan.FromHours(6)).ToList();")]
    [InlineData("var days = 3; var when = new DateTime(2024, 3, 1); var r = db.Orders.Where(o => o.CreatedAt.AddDays(days) > when && o.CreatedAt.AddMonths(1).AddYears(-1) < when).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.AddHours(1.5) > o.CreatedAt.AddMinutes(o.Quantity).AddSeconds(1).AddMilliseconds(2)).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.ShippedAt!.Value.Year == 2024 && o.ShippedAt.Value.AddDays(1) > o.CreatedAt).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.Day.Year == 2024 && o.Day.AddDays(1).Month == 3 && o.Day.DayNumber > 738000 && DateOnly.FromDateTime(o.CreatedAt) == o.Day).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.At.Hour > 6 && o.At.AddHours(2).Minute == 0).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.Due.Year == 2024 && o.Due.AddDays(1) > DateTimeOffset.UtcNow).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt < DateTime.Now && o.CreatedAt < DateTime.UtcNow.AddDays(-1) && o.CreatedAt >= DateTime.Today).ToList();")]
    [InlineData("var r = db.Orders.Where(o => EF.Functions.DateDiffDay(o.CreatedAt, o.ShippedAt) > 1 && EF.Functions.DateDiffMonth(o.CreatedAt, DateTime.Now) < 12).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Abs(o.Quantity) > 2 && Math.Abs(o.Price) < 10m && Math.Abs(o.Rating) > 0.5).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Round(o.Price) == 3m && Math.Round(o.Price, 2) > 1m && decimal.Round(o.Price, 1) > 0m && Math.Round(o.Rating) > 1).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Floor(o.Price) + Math.Ceiling(o.Price) + Math.Truncate(o.Price) + decimal.Floor(o.Price) > 0m).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Pow(o.Rating, 2) + Math.Sqrt(o.Views) + Math.Log(o.Rating) + Math.Log10(o.Rating) + Math.Exp(o.Rating) > 1).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Sin(o.Rating) + Math.Cos(o.Rating) + Math.Tan(o.Rating) + Math.Asin(o.Rating) + Math.Acos(o.Rating) + Math.Atan(o.Rating) + Math.Atan2(o.Rating, 1) > 0).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Sign(o.Quantity) == -1 || Math.Sign(o.Price) == 1).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.Discount.HasValue && Math.Abs(o.Discount.Value) > 1m).ToList();")]
    [InlineData("var r = db.Orders.OrderBy(o => o.CreatedAt.Year).ThenByDescending(o => Math.Abs(o.Quantity)).ToList();")]
    [InlineData("var r = db.Orders.Count(o => o.CreatedAt.Year == 2024);")]
    public void DateAndMathFunctions_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(Source(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void DatePart_IsAFunctionOfTheColumn()
    {
        var generated = GeneratorHost.Run(Source("var r = db.Orders.Where(o => o.CreatedAt.Year == 2024).ToList();")).Generated(InterceptorEmitter.HintName);

        Assert.Contains(
            $"new {Templates}.FunctionValueTemplate({Templates}.ValueFunction.Year, new {Templates}.PropertyValueTemplate(\"CreatedAt\"))",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_IsTheDatabasesClock_NotAValue()
    {
        var generated = GeneratorHost.Run(Source("var r = db.Orders.Where(o => o.CreatedAt < DateTime.Now.AddDays(-7)).ToList();")).Generated(InterceptorEmitter.HintName);

        Assert.Contains(
            $"new {Templates}.FunctionValueTemplate({Templates}.ValueFunction.AddDays, new {Templates}.FunctionValueTemplate({Templates}.ValueFunction.Now), ",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.DateTime.Now", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundWithDecimals_ReadsTheDecimalsFromASlot()
    {
        var generated = GeneratorHost.Run(Source("var digits = 2; var r = db.Orders.Where(o => Math.Round(o.Price, digits) > 1m).ToList();")).Generated(InterceptorEmitter.HintName);

        Assert.Contains(
            $"new {Templates}.FunctionValueTemplate({Templates}.ValueFunction.Round, new {Templates}.PropertyValueTemplate(\"Price\"), new {Templates}.SlotValueTemplate(0))",
            generated,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.DayOfWeek == DayOfWeek.Monday).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.Ticks > 0).ToList();")]
    [InlineData("var r = db.Orders.Where(o => o.CreatedAt.AddTicks(1) > o.CreatedAt).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Max(o.Quantity, 1) == 2).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Round(o.Price, MidpointRounding.AwayFromZero) == 3m).ToList();")]
    [InlineData("var r = db.Orders.Where(o => Math.Log(o.Rating, 2) > 1).ToList();")]
    [InlineData("var r = db.Orders.Where(o => (o.ShippedAt!.Value - o.CreatedAt).TotalDays > 1).ToList();")]
    public void FunctionsEfCoreDoesNotTranslate_AreReported(string body)
        => Assert.NotEmpty(GeneratorHost.Run(Source(body)).Diagnostics);

    private static string Source(string body)
        => TestSources.Usings + "namespace App;\n\n" + Model + $$"""
            public static class Queries
            {
                public static async Task RunAsync(OrderDb db, CancellationToken ct)
                {
                    await Task.CompletedTask;
                    {{body}}
                }
            }
            """;
}
