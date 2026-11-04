using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class ProjectionDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, b.Name }).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => b.Author).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => new { b, AuthorId = (int?)b.Author!.Id }).Skip(1).Take(2).ToList();")]
    [InlineData("var r = await db.Blogs.Select(b => b.Name).FirstOrDefaultAsync(ct);")]
    [InlineData("var r = db.Posts.Select(p => p.Blog.Author).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => ValueTuple.Create(b.Id, b.Status)).ToArray();")]
    [InlineData("var q = db.Blogs.Select(b => b.Id); var r = q.Count();")]
    [InlineData("var r = db.Blogs.Include(b => b.Posts).Select(b => new { b, b.Name }).ToList();")]
    [InlineData("foreach (var name in db.Blogs.Select(b => b.Name)) { }")]
    public void SupportedProjection_ReportsNothing(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);

    [Theory]
    [InlineData("var r = db.Blogs.Select(b => b.Name + \"!\").ToList();", "b.Name + \"!\"")]
    [InlineData("var r = db.Blogs.Select(b => b.Name.ToUpper()).ToList();", "b.Name.ToUpper()")]
    [InlineData("var x = 1; var r = db.Blogs.Select(b => new { b.Id, x }).ToList();", "x")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id, One = 1 }).ToList();", "1")]
    [InlineData("var r = db.Blogs.Select(b => b.Posts).ToList();", "b.Posts")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Posts.Count }).ToList();", "b.Posts.Count")]
    public void UnsupportedProjection_ReportsKP2009AtTheExpression(string body, string expression)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2009", diagnostic.Id);
        Assert.Equal(expression, run.TextAt(diagnostic));
    }

    [Fact]
    public void TypeTheGeneratedCodeCannotName_ReportsKP2009()
    {
        var source = TestSources.Usings + "namespace App;\n\n" + TestSources.Model + """
            public static class Queries
            {
                private sealed class Row
                {
                    public int Id { get; set; }
                }

                public static int Run(AppDb db) => db.Blogs.Select(b => new Row { Id = b.Id }).ToList().Count;
            }
            """;

        var diagnostic = Assert.Single(GeneratorHost.Run(source).Diagnostics);

        Assert.Equal("KP2009", diagnostic.Id);
        Assert.Contains("accessible", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = db.Blogs.Select(b => b.Id).Where(i => i > 1).ToList();", "Where")]
    [InlineData("var r = db.Blogs.Select(b => b.Name).OrderBy(n => n).ToList();", "OrderBy")]
    [InlineData("var r = db.Blogs.Select(b => b.Id).First(i => i > 1);", "First")]
    [InlineData("var r = await db.Blogs.Select(b => b.Id).AnyAsync(i => i > 1, ct);", "AnyAsync")]
    [InlineData("var r = db.Blogs.Select(b => b.Id).Distinct().ToList();", "Distinct")]
    [InlineData("var r = db.Blogs.Select(b => b.Rating).Sum();", "Sum")]
    [InlineData("var r = db.Blogs.Select(b => b.Author).Select(a => a!.Id).ToList();", "Select")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Id }).AsNoTracking().ToList();", "AsNoTracking")]
    [InlineData("var q = db.Blogs.Select(b => b.Id); var r = q.Where(i => i > 0).ToList();", "Where")]
    public void OperatorAfterSelect_ReportsKP1007AtOperatorName(string body, string operatorName)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1007", diagnostic.Id);
        Assert.Equal(operatorName, run.TextAt(diagnostic));
    }

    [Fact]
    public void IndexedSelector_ReportsKP1006()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Select((b, i) => b.Id).ToList();"));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1006", diagnostic.Id);
        Assert.Equal("Select", run.TextAt(diagnostic));
    }

    [Theory]
    [InlineData("var r = new List<int>().AsQueryable().Select(x => x + 1).Where(x => x > 1).ToList();")]
    [InlineData("var r = new List<Request>().AsQueryable().Select(x => x.Min).Distinct().ToList();")]
    public void ProjectionOfANonEntityQuery_ReportsNothing(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);
}
