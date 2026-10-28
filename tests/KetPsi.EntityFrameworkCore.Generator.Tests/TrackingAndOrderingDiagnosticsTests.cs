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
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name.Length).ToList();", "b.Name.Length")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => -b.Rating).ToList();", "-b.Rating")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Id).ThenBy(b => b.Author!.Id).ToList();", "b.Author!.Id")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderByDescending(b => b.Author).ToList();", "b.Author")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => 1).ToList();", "1")]
    public void UnsupportedOrderKey_ReportsKP2008AtKey(string body, string key)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2008", diagnostic.Id);
        Assert.Equal(key, run.TextAt(diagnostic));
        Assert.Equal($"The order key '{key}' must be a property of the entity itself, such as x => x.Name", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => (b.Name)).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Views).ThenByDescending(b => b.Status).ToList();")]
    public void DirectPropertyKey_IsAccepted(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);
}
