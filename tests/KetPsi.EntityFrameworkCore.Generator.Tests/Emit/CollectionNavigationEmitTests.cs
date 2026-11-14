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
    [InlineData("var r = db.Blogs.AsNoTracking().OrderByDescending(b => b.Posts.Count).ThenBy(b => b.Posts.Sum(p => p.Id)).ToList();")]
    [InlineData("var min = 1; var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Count(p => p.Id > min) > min).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Posts.LongCount() > 1L && b.Posts.Average(p => p.Id) > 2.5).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Max(p => p.Blog.Price) > b.Price).Skip(1).Take(2).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Posts.Select(p => p.Id).Min() == b.Rating).ToList();")]
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
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, Count = b.Posts.Count() }).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => b.Posts.Any()).ToList();")]
    [InlineData("var min = 2; var r = db.Blogs.Select(b => new { b.Name, Some = b.Posts.Count(p => p.Id > min), All = b.Posts.All(p => p.Id > b.Rating) }).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Posts.Count, Long = b.Posts.LongCount(), Avg = b.Posts.Average(p => p.Id), Max = (int?)b.Posts.Max(p => p.Id), Sum = b.Posts.Sum(p => p.Blog.Price) }).ToList();")]
    [InlineData("var bonus = 1; var r = db.Blogs.Select(b => new { b.Id, Next = b.Posts.Count() * 2 + bonus, Label = b.Name + b.Posts.Count }).ToList();")]
    [InlineData("var r = db.Posts.Select(p => new { p.Id, Siblings = p.Blog.Posts.Count(q => q.Id != p.Id) }).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => new { b, Count = b.Posts.Count }).ToList();")]
    [InlineData("var r = await db.Blogs.Select(b => b.Posts.Count()).Distinct().ToListAsync(ct);")]
    [InlineData("var r = db.Blogs.OrderBy(b => b.Id).Skip(1).Take(2).Select(b => new { b.Name, C = b.Posts.Count() }).FirstOrDefault();")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, C = b.Posts.Count() }).Count();")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, Max = b.Posts.Select(p => p.Id).Max() }).OrderBy(x => x.Id).Skip(1).ToList();")]
    [InlineData("var r = db.Blogs.Where(b => b.Posts.Any()).Select(b => new { b.Id, Max = b.Posts.Where(p => p.Id > 1).Max(p => (int?)p.Id) }).ToList();")]
    public void CollectionValuesInProjections_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void ValuesInAProjection_AreReadAfterTheColumnsAndPassedToTheRuntime()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Select(b => new { C = b.Posts.Count(), b.Name, M = b.Posts.Max(p => p.Id) }).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains(
            "new global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.ValueTemplate[] { new global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.CountValueTemplate(",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("C = (reader.IsDBNull(ordinals[1])", generated, StringComparison.Ordinal);
        Assert.Contains("Name = (reader.IsDBNull(ordinals[0]) ? null! : reader.GetString(ordinals[0]))", generated, StringComparison.Ordinal);

        // Max of no posts is null, which an int cannot hold, as in EF Core.
        Assert.Contains(
            "M = (reader.IsDBNull(ordinals[2]) ? throw new global::System.InvalidOperationException(global::KetPsi.EntityFrameworkCore.Infrastructure.NullValue.Message) : reader.GetInt32(ordinals[2]))",
            generated,
            StringComparison.Ordinal);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("new { L = b.Name + suffix, C = b.Posts.Count(p => p.Id > min) }", "((string)captures[0]!)", "new object?[] { (((int)captures[1]!)) }")]
    [InlineData("new { C = b.Posts.Count(p => p.Id > min), L = b.Name + suffix }", "((string)captures[1]!)", "new object?[] { (((int)captures[0]!)) }")]
    public void CapturesOfValuesAndComputedMembers_AreNumberedInSourceOrder(string selector, string suffix, string slots)
    {
        var run = GeneratorHost.Run(TestSources.WithBody($"var suffix = \"!\"; var min = 2; var r = db.Blogs.Select(b => {selector}).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);
        Assert.Contains("var captures = global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues.ReadAll(selector);", generated, StringComparison.Ordinal);
        Assert.Contains(suffix, generated, StringComparison.Ordinal);
        Assert.Contains(slots, generated, StringComparison.Ordinal);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Blogs.Select(b => b.Posts.Count()).Sum();", "KP1007")]
    [InlineData("var r = db.Blogs.Select(b => b.Posts.Max(p => p.Blog.Status)).ToList();", "KP2009")]
    [InlineData("var r = db.Blogs.Select(b => b.Posts.Distinct().Count()).ToList();", "KP2005")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, C = b.Posts.Count() }).Where(x => x.C > 1).ToList();", "KP2001")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, b.Posts }).ToList();", "KP2009")]
    [InlineData("var r = db.Blogs.Join(db.Posts, b => b.Id, p => p.BlogId, (b, p) => new { b.Id, C = b.Posts.Count() }).ToList();", "KP2009")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => g.Sum(b => b.Posts.Count())).ToList();", "KP2011")]
    public void UnsupportedCollectionValuesInProjections_AreReported(string body, string id)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Equal(id, Assert.Single(run.Diagnostics).Id);
    }

    [Theory]
    [InlineData("var r = db.Blogs.Where(b => b.Id > 1).Select(b => new { b.Id, b.Posts }).Where(x => x.Posts.Any()).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Where(g => g.Any(b => b.Posts.Any())).Select(g => g.Key).ToList();")]
    public void CollectionsOutsideTheEntityPredicate_AreStillReported(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.NotEmpty(run.Diagnostics);
    }
}
