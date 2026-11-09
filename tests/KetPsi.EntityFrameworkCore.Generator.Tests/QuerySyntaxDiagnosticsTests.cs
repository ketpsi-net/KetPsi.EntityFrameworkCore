using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class QuerySyntaxDiagnosticsTests
{
    // Query syntax has no invocation an interceptor can replace, so it is reported instead of running through EF Core.
    [Theory]
    [InlineData("var r = (from b in db.Blogs where b.IsActive select b.Name).ToList();")]
    [InlineData("var r = (from p in db.Posts join b in db.Blogs on p.BlogId equals b.Id where b.Rating > 2 select new { p.Id, b.Name }).ToList();")]
    [InlineData("var r = (from b in db.Blogs.Where(x => x.IsActive) orderby b.Name select b).ToList();")]
    public void QuerySyntax_ReportsKP1009AtFrom(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics, static d => d.Id == "KP1009");
        Assert.Equal("from", run.TextAt(diagnostic));
        Assert.StartsWith("Query syntax over an entity query cannot be intercepted", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void QuerySyntax_OverObjects_ReportsNothing()
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody("int[] items = [1, 2]; var r = (from i in items where i > 1 select i).ToList();")).Diagnostics);
}
