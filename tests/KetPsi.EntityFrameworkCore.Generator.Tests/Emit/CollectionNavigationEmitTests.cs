using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

/// <summary>
/// Predicates and order keys on collection navigations: their templates, and the values their conditions compute from
/// the captured variables, compile in the generated code.
/// </summary>
public sealed class CollectionNavigationEmitTests
{
    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Any()).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => !b.Posts.Any(p => p.Id > b.Rating)).ToList();")]
    [InlineData("var min = 2; var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Where(p => p.Id > min).All(p => p.BlogId == b.Id)).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().Where(p => p.Blog.Posts.Any(q => q.Id != p.Id)).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Blog.Posts.Any(q => q.Id > b.Rating))).ToList();")]
    [InlineData("var r = db.Blogs.Count(b => !b.Posts.Any());")]
    [InlineData("var r = await db.Blogs.AnyAsync(b => b.Posts.Any(p => p.Blog.Author == null), ct);")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Posts.Any()).ThenBy(b => b.Id).ToList();")]
    [InlineData("var on = true; var r = db.Blogs.WhereIf(on, b => b.Posts.Any()).ToList();")]
    [InlineData("var r = db.Blogs.Where(b => b.Posts.Any()).Select(b => new { b.Id, b.Name }).ToList();")]
    [InlineData("var r = db.Blogs.Where(b => !b.Posts.Any()).ExecuteDelete();")]
    [InlineData("var r = db.Blogs.Include(b => b.Posts).Where(b => b.Posts.Any(p => p.Id > 1)).ToList();")]
    public void CollectionShapes_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void ValueInACondition_IsComputedFromTheCaptures()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var min = 2; var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Id > min)).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains(
            "new global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.ExistsTemplate(new global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.CollectionTemplate(\"Posts\"",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("var captures = global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues.ReadAll(predicate);", generated, StringComparison.Ordinal);
        Assert.Contains("new object?[] { (((int)captures[0]!)) }", generated, StringComparison.Ordinal);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Blogs.Where(b => b.Id > 1).Select(b => new { b.Id, b.Posts }).Where(x => x.Posts.Any()).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Where(g => g.Any(b => b.Posts.Any())).Select(g => g.Key).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Posts.Count).ToList();")]
    public void CollectionsOutsideTheEntityPredicate_AreStillReported(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.NotEmpty(run.Diagnostics);
    }
}
