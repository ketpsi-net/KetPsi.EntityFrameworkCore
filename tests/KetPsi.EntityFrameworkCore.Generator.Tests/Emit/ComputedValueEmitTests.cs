using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

/// <summary>
/// Predicates and order keys on computed values: their templates, and the values the interceptor reads from the tree
/// or computes from the captured variables, compile in the generated code.
/// </summary>
public sealed class ComputedValueEmitTests
{
    private const string ReadAll = "var captures = global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues.ReadAll(predicate);";

    [Fact]
    public void ComputedValue_IsComputedFromTheCapturesTheLambdaHolds()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var min = 2; var r = db.Blogs.AsNoTracking().Where(b => b.Rating > min + 1).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains(ReadAll, generated, StringComparison.Ordinal);
        Assert.Contains("new object?[] { (((int)captures[0]!) + 1) }", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("var body", generated, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void TreeValuesAndComputedValues_AreReadTogether()
    {
        var run = GeneratorHost.Run(TestSources.WithBody(
            "var min = 2; var name = \"x\"; var r = db.Blogs.AsNoTracking().Where(b => b.Name == name && b.Rating > min * 2).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains("var body = predicate.Body;", generated, StringComparison.Ordinal);
        Assert.Contains(ReadAll, generated, StringComparison.Ordinal);
        Assert.Contains("(((int)captures[1]!) * 2) }", generated, StringComparison.Ordinal);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void PropertyComparisons_ReadNoValues()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsNoTracking().Where(b => b.Rating > b.Id && b.Views != b.Rating).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains("ValueComparisonTemplate", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Array.Empty<object?>()", generated, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Blogs.Count(b => b.Price * b.Rating > 100m);")]
    [InlineData("var r = await db.Blogs.AnyAsync(b => (b.Views ?? 0) > b.Rating, ct);")]
    [InlineData("var on = true; var min = 1; var r = db.Blogs.WhereIf(on, b => b.Rating - min > b.Id).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.IsActive ? b.Rating > 1 : b.Rating < 1).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.IsActive ^ (b.Views == null)).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Name + \"!\" + b.Rating == \"x!1\").ToList();")]
    [InlineData("var d = DateTime.UtcNow; var r = db.Blogs.AsNoTracking().Where(b => b.Price > (decimal)d.AddDays(1).Day).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Status == (Limits.Max > 2 ? Status.Draft : Status.Published)).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().Where(p => p.Blog.Rating + p.Id > p.BlogId).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().Where(p => p.Blog.Author == null ? p.Id > 1 : p.Blog.Author.Id > 1).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => true).ToList();")]
    [InlineData("var r = db.Blogs.Where(b => b.Id > 1).Select(b => new { b.Id, b.Rating }).Where(x => x.Rating * 2 > x.Id).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Where(g => g.Count() > g.Key + 1).Select(g => g.Key).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Price * b.Rating).ThenByDescending(b => b.Views ?? 0).ToList();")]
    [InlineData("var k = 2; var r = db.Blogs.AsNoTracking().OrderBy(b => b.Rating % (k + 1)).ToList();")]
    public void ComputedShapes_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void ComputedOrderKey_IsAValueTemplateWithItsValues()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var k = 2; var r = db.Blogs.AsNoTracking().OrderBy(b => b.Rating % (k + 1)).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName).ReplaceLineEndings("\n");
        Assert.Contains("private static readonly global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.ValueTemplate Template", generated, StringComparison.Ordinal);
        Assert.Contains("CapturedValues.ReadAll(keySelector);", generated, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(source, Template", generated, StringComparison.Ordinal);
        Assert.Contains("new object?[] { (((int)captures[0]!) + 1) }", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void MembersOfTheEnclosingInstance_AreCaptured()
    {
        var run = GeneratorHost.Run(TestSources.Usings + "namespace App;\n\n" + TestSources.Model + """
            public sealed class Repository(AppDb db)
            {
                private readonly int _min = 2;

                private int Limit { get; } = 5;

                public List<Blog> Run() => db.Blogs.AsNoTracking().Where(b => b.Rating > _min + Limit && b.Id < this.Limit * 2).ToList();
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        Assert.Contains("(((int)captures[0]!) + ((int)captures[1]!))", run.Generated(InterceptorEmitter.HintName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private static int Twice(int v) => v * 2;", "b => b.Rating > Twice(1)", "Twice(1)")]
    [InlineData("private int Half(int v) => v / 2;", "b => b.Rating > Half(4) + 1", "Half(4)")]
    [InlineData("private sealed class Box { public int Value = 1; }\n    private readonly Box _box = new();", "b => b.Rating > _box.Value + 1", "_box.Value")]
    public void ValueTheGeneratedCodeCannotCompute_ReportsKP2007(string member, string predicate, string text)
    {
        var run = GeneratorHost.Run(TestSources.Usings + "namespace App;\n\n" + TestSources.Model + $$"""
            public sealed class Repository(AppDb db)
            {
                {{member}}

                public List<Blog> Run() => db.Blogs.AsNoTracking().Where({{predicate}}).ToList();
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2007", diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
    }

    [Fact]
    public void QueryFilterComparingProperties_IsTranslated()
    {
        var run = GeneratorHost.Run(TestSources.Usings + "namespace App;\n\n" + TestSources.Model.Replace(
            "public DbSet<Post> Posts => Set<Post>();",
            "public DbSet<Post> Posts => Set<Post>();\n    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Blog>().HasQueryFilter(x => x.Rating >= x.Id && (x.Views ?? 0) > 0);",
            StringComparison.Ordinal));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        Assert.Contains("ValueComparisonTemplate", run.Generated(QueryFilterEmitter.HintName), StringComparison.Ordinal);
    }
}
