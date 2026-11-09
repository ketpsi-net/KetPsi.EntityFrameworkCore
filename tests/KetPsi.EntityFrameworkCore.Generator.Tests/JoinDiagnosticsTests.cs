using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class JoinDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name, Post = p }).ToList();")]
    [InlineData("var min = 2; var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Where(x => x.b.Rating > min && x.p.Id != 3).OrderBy(x => x.b.Name).ThenByDescending(x => x.p.Id).Select(x => new { x.p.Id, x.b.Name }).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Join(db.Posts, x => x.b.Id, o => o.BlogId, (x, o) => new { x.p.Id, Other = o.Id }).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Join(db.Posts, x => x.b.Id, o => o.BlogId, (x, o) => new { x, o }).Where(y => y.o.Id > 1 || y.x.b.IsActive).Select(y => y.x.p).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs.Where(b => b.IsActive).AsNoTracking(), p => p.BlogId, b => b.Id, (p, b) => b).ToList();")]
    [InlineData("IQueryable<Blog> active = db.Blogs.Where(b => b.IsActive); var r = db.Posts.Join(active, p => p.BlogId, b => b.Id, (p, b) => b.Name).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => new { A = p.BlogId, B = p.Id }, b => new { A = b.Id, B = b.Rating }, (p, b) => p.Id).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => (int?)p.BlogId, b => (int?)b.Id, (p, b) => p.Id).ToList();")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).OrderBy(x => x.Name).Skip(1).Take(2).Count();")]
    [InlineData("var r = await db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => p.Id).FirstAsync(ct);")]
    [InlineData("var r = db.Posts.Join(db.Set<Author>(), p => p.Blog.Author!.Id, a => a.Id, (p, a) => new { p.Id, A = a.Id }).ToList();")]
    [InlineData("var suffix = \"!\"; var r = db.Posts.Where(p => p.Id > 1).OrderBy(p => p.Id).Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => b.Name + suffix).ToList();")]
    [InlineData("var r = db.Posts.Select(p => new { p, p.Blog }).Where(x => x.Blog.Rating > 1).OrderBy(x => x.p.Id).Select(x => x.p.Id).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => b.Id).Where(i => i > 1).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => b.Name).OrderBy(n => n).ToList();")]
    [InlineData("var r = db.Blogs.Select(b => b.Author).Select(a => a!.Id).ToList();")]
    public void SupportedJoin_GeneratesCompilingCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Posts.Join(new List<Blog>(), p => p.BlogId, b => b.Id, (p, b) => p.Id).ToList();", "KP2012", "new List<Blog>()")]
    [InlineData("var r = db.Posts.Join(db.Blogs.OrderBy(b => b.Id), p => p.BlogId, b => b.Id, (p, b) => p.Id).ToList();", "KP2012", "db.Blogs.OrderBy(b => b.Id)")]
    [InlineData("var r = db.Blogs.Join(db.Posts, b => b.Id, p => p.Blog.Id, (b, p) => p.Id).ToList();", "KP2012", "p.Blog.Id")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId + 1, b => b.Id, (p, b) => p.Id).ToList();", "KP2012", "p.BlogId + 1")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => b.Author!.Id).ToList();", "KP2009", "b.Author!.Id")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Take(2).Where(x => x.b.Rating > 1).ToList();", "KP1007", "Where")]
    [InlineData("var r = db.Posts.Select(p => new { L = p.Id + 1 }).Where(x => x.L > 1).ToList();", "KP1007", "Where")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Where(x => x.b.Name.StartsWith(\"a\")).ToList();", "KP2001", "x.b.Name.StartsWith(\"a\")")]
    [InlineData("var r = db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => p.Id, EqualityComparer<int>.Default).ToList();", "KP1006", "Join")]
    [InlineData("var r = db.Blogs.GroupJoin(db.Posts, b => b.Id, p => p.BlogId, (b, ps) => b).ToList();", "KP1001", "GroupJoin")]
    [InlineData("Expression<Func<Post, int>> key = p => p.BlogId; var r = db.Posts.Join(db.Blogs, key, b => b.Id, (p, b) => p.Id).ToList();", "KP1005", "key")]
    public void UnsupportedJoin_ReportsAtTheConstruct(string body, string id, string text)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
    }
}
