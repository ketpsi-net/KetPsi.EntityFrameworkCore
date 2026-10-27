using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class TrackingAndOrderingDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Blogs.ToList();", "ToList")]
    [InlineData("var r = db.Blogs.ToArray();", "ToArray")]
    [InlineData("var r = db.Blogs.Where(b => b.IsActive).OrderBy(b => b.Id).First();", "First")]
    [InlineData("var r = db.Blogs.Take(1).SingleOrDefault(b => b.Id == 1);", "SingleOrDefault")]
    [InlineData("var r = await db.Blogs.FirstOrDefaultAsync(ct);", "FirstOrDefaultAsync")]
    [InlineData("var r = await db.Blogs.IgnoreQueryFilters().ToListAsync(ct);", "ToListAsync")]
    [InlineData("var r = db.Set<Blog>().ToList();", "ToList")]
    [InlineData("var r = (db.Blogs).Where(b => b.IsActive).ToList();", "ToList")]
    public void VisibleTrackingQuery_ReportsKP1003AtTerminal(string body, string terminal)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1003", diagnostic.Id);
        Assert.Equal(terminal, run.TextAt(diagnostic));
        Assert.Equal(
            $"'{terminal}' runs a tracking query. KetPsi.EntityFrameworkCore only runs no-tracking queries; add AsNoTracking() " +
            "or set the context's QueryTrackingBehavior to NoTracking.",
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().ToList();")]
    [InlineData("var r = db.Blogs.Where(b => b.IsActive).AsNoTracking().OrderBy(b => b.Id).First();")]
    [InlineData("var r = db.Blogs.Count();")]
    [InlineData("var r = await db.Blogs.AnyAsync(b => b.IsActive, ct);")]
    [InlineData("IQueryable<Blog> q = db.Blogs; var r = q.ToList();")]
    public void NoVisibleTrackingQuery_ReportsNothing(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);

    [Fact]
    public void ContextDefaultTrackingConfigured_LeavesCheckToRuntime()
    {
        var source = TestSources.WithBody("var r = db.Blogs.ToList();") + """

            public static class Setup
            {
                public static void Configure(DbContextOptionsBuilder builder)
                    => builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            }
            """;

        Assert.Empty(GeneratorHost.Run(source).Diagnostics);
    }

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

    [Theory]
    [InlineData("UseQueryTrackingBehavior", true)]
    [InlineData("QueryTrackingBehavior", true)]
    [InlineData("AsNoTracking", false)]
    public void IsTrackingBehaviorReference_MatchesConfigurationNames(string identifier, bool expected)
        => Assert.Equal(expected, QueryInterceptorGenerator.IsTrackingBehaviorReference(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName(identifier)));
}
