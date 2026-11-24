using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

/// <summary>
/// <c>ExecuteUpdate</c> with EF Core 10's setters builder, which these tests compile against: the call and each of its
/// <c>SetProperty</c> calls are intercepted, and the setters are recorded while the delegate runs.
/// </summary>
public sealed class ExecuteUpdateEmitTests
{
    private const string Model = """
        public enum Grade { A, B }

        public sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed class Order
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? Note { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public int? Version { get; set; }
            public DateTime CreatedAt { get; set; }
            public Grade Grade { get; set; }
            public bool IsActive { get; set; }
            public int? CustomerId { get; set; }
            public Customer? Customer { get; set; }
        }

        public sealed class OrderDb : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        """;

    [Theory]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Name, \"x\"));")]
    [InlineData("var label = \"x\"; var r = db.Orders.Where(o => o.Id > 1).ExecuteUpdate(s => s.SetProperty(o => o.Note, label).SetProperty(o => o.Version, (int?)null));")]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Quantity, o => o.Quantity + 1).SetProperty(o => o.Price, o => Math.Round(o.Price * 1.1m, 2)));")]
    [InlineData("var days = 3; var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.CreatedAt, o => o.CreatedAt.AddDays(days)));")]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Note, o => o.Quantity > 5 ? \"big\" : o.Name.ToUpper()).SetProperty(o => o.IsActive, o => !o.IsActive));")]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Grade, Grade.B));")]
    [InlineData("var flag = true; var r = db.Orders.ExecuteUpdate(s => { s.SetProperty(o => o.Version, 1); if (flag) { s.SetProperty(o => o.Note, \"set\"); } });")]
    [InlineData("var r = db.Orders.OrderBy(o => o.Id).Skip(1).Take(2).ExecuteUpdate(s => s.SetProperty(o => o.Version, 9));")]
    [InlineData("var r = db.Orders.Where(o => o.Customer!.Name == \"Ann\").ExecuteUpdate(s => s.SetProperty(o => o.Version, o => o.Quantity));")]
    [InlineData("var r = await db.Orders.Where(o => o.Id == 4).ExecuteUpdateAsync(s => s.SetProperty(o => o.Name, \"async\"), ct);")]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Note, o => o.Customer!.Name));")]
    public void ExecuteUpdateShapes_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(Source(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void ExecuteUpdate_RecordsTheSetters_AndEachSetPropertyAddsItsOwn()
    {
        var generated = GeneratorHost.Run(Source("var min = 1; var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Name, \"x\").SetProperty(o => o.Quantity, o => o.Quantity + min));"))
            .Generated(InterceptorEmitter.HintName);

        Assert.Contains(
            "var setters = global::KetPsi.EntityFrameworkCore.Infrastructure.UpdateSetters.Record(setPropertyCalls, new global::Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<global::App.Order>());",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("return global::KetPsi.EntityFrameworkCore.Infrastructure.SqlQuery.ExecuteUpdate(source, setters);", generated, StringComparison.Ordinal);
        Assert.Contains(
            "global::KetPsi.EntityFrameworkCore.Infrastructure.UpdateSetters.TryAdd(new global::KetPsi.EntityFrameworkCore.Infrastructure.UpdateSetter(\"Name\", new global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.SlotValueTemplate(0), new object?[] { valueExpression }))",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("var captures = global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues.ReadAll(valueExpression);", generated, StringComparison.Ordinal);
        Assert.Contains(": builder.SetProperty(propertyExpression, valueExpression);", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Customer!.Name, \"z\"));", "KP2013")]
    [InlineData("var r = db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Note, o => Describe(o.Name)));", "KP2003")]
    public void UnsupportedSetters_AreReported(string body, string id)
        => Assert.Contains(GeneratorHost.Run(Source(body)).Diagnostics, d => d.Id == id);

    private static string Source(string body)
        => TestSources.Usings + "namespace App;\n\n" + Model + $$"""
            public static class Queries
            {
                public static string Describe(string value) => value;

                public static async Task RunAsync(OrderDb db, CancellationToken ct)
                {
                    await Task.CompletedTask;
                    {{body}}
                }
            }
            """;
}
