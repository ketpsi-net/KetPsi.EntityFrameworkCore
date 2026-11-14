using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class TrackingAndOrderingDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Blogs.ToList();")]
    [InlineData("var r = db.Blogs.ToArray();")]
    [InlineData("var r = db.Blogs.Where(b => b.IsActive).OrderBy(b => b.Id).First();")]
    [InlineData("var r = db.Blogs.Take(1).SingleOrDefault(b => b.Id == 1);")]
    [InlineData("var r = await db.Blogs.FirstOrDefaultAsync(ct);")]
    [InlineData("var r = await db.Blogs.IgnoreQueryFilters().ToListAsync(ct);")]
    [InlineData("var r = db.Set<Blog>().ToList();")]
    [InlineData("var r = db.Blogs.AsTracking().ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().AsTracking(QueryTrackingBehavior.TrackAll).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTrackingWithIdentityResolution().ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().ToList();")]
    [InlineData("IQueryable<Blog> q = db.Blogs; var r = q.ToList();")]
    public void TrackingAndNoTrackingQueries_ReportNothing(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Rating << 1).ToList();", "b.Rating << 1")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name + b.Status).ToList();", "b.Name + b.Status")]
    public void UnsupportedOrderKey_ReportsKP2008AtKey(string body, string key)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2008", diagnostic.Id);
        Assert.Equal(key, run.TextAt(diagnostic));
        Assert.Equal(
            $"The order key '{key}' must be a property of the entity or of a reference navigation, or a value computed from them, " +
            "such as x => x.Name, x => x.Blog.Name or x => x.Price * x.Quantity",
            diagnostic.GetMessage());
    }

    // A key the value translation rejects for a precise reason reports that reason.
    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name.PadLeft(3)).ToList();", "KP2003", "b.Name.PadLeft(3)")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Id).ThenBy(b => b.Posts.Distinct().Count()).ToList();", "KP2005", "b.Posts.Distinct().Count()")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderByDescending(b => b.Author).ToList();", "KP2002", "b.Author")]
    public void OrderKeyWithAnUnsupportedPart_ReportsThatPart(string body, string id, string text)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
    }

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => -b.Rating).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Price * b.Rating).ThenByDescending(b => b.Views ?? 0).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.IsActive ? 0 : 1).ThenBy(b => b.Rating > 3).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().OrderBy(p => p.Blog.Name + p.Id).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => 1).ToList();")]
    [InlineData("var k = 2; var r = db.Blogs.AsNoTracking().OrderBy(b => b.Rating % k).ToList();")]
    public void ComputedOrderKey_IsTranslated(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        Assert.Contains("ValueTemplate", run.Generated(KetPsi.EntityFrameworkCore.Generator.Emit.InterceptorEmitter.HintName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => (b.Name)).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Views).ThenByDescending(b => b.Status).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Id).ThenBy(b => b.Author!.Id).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().OrderByDescending(p => (p.Blog).Rating).ThenBy(p => p.Blog.Author!.Id).ToList();")]
    public void DirectPropertyKey_IsAccepted(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);
}
