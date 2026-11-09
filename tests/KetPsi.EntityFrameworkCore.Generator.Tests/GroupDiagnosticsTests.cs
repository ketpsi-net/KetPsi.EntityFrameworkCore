using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class GroupDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => g.Key).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, S = g.Sum(b => b.Rating), A = g.Average(b => b.Price), Mi = g.Min(b => b.Name), Ma = g.Max(b => b.Views), L = g.LongCount() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => new { b.Status, b.IsActive }).Select(g => new { g.Key.Status, g.Key, C = g.Count() }).ToList();")]
    [InlineData("var r = db.Posts.GroupBy(p => p.Blog.Name).Select(g => new { g.Key, C = g.Count() }).ToList();")]
    [InlineData("var min = 3; var r = db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Active = g.Count(b => b.IsActive && b.Rating > min), Rated = g.Where(b => b.Rating > 1).Count() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => g.Select(b => b.Rating).Distinct().Count()).ToList();")]
    [InlineData("var bonus = 2; var r = db.Blogs.GroupBy(b => b.Status).Select(g => new { Label = g.Key + \"!\", S = g.Sum(b => b.Rating) + bonus }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Where(g => g.Count() > 1 && g.Key != 3).Select(g => g.Key).ToList();")]
    [InlineData("var min = 2; var r = db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, C = g.Count() }).Where(x => x.C > min).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new { g.Key, C = g.Count() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, C = g.Count() }).OrderBy(x => x.C).ThenByDescending(x => x.Key).Skip(1).Take(2).ToList();")]
    [InlineData("var r = await db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, C = g.Count() }).OrderBy(x => x.Key).FirstAsync(ct);")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Count();")]
    [InlineData("var r = await db.Blogs.GroupBy(b => b.Rating).LongCountAsync(ct);")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Any(g => g.Count() > 1);")]
    [InlineData("var r = await db.Blogs.GroupBy(b => b.Rating).AnyAsync(g => g.Sum(b => b.Price) > 10m, ct);")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count()).Distinct().ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count()).Max();")]
    [InlineData("var r = db.Blogs.Where(b => b.IsActive).OrderBy(b => b.Id).Take(3).GroupBy(b => b.Status).Select(g => new { g.Key, C = g.Count() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status, b => b.Rating).Select(g => new { g.Key, S = g.Sum(), M = g.Max() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status, (k, g) => new { k, C = g.Count() }).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status, b => b.Price, (k, g) => new { k, S = g.Sum() }).OrderBy(x => x.S).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).ToList();")]
    [InlineData("var r = await db.Blogs.GroupBy(b => b.Status, b => b.Name).ToArrayAsync(ct);")]
    [InlineData("var r = db.Blogs.GroupBy(b => new { b.Status, b.IsActive }).ToList();")]
    [InlineData("foreach (var g in db.Blogs.GroupBy(b => b.Status)) { }")]
    [InlineData("var q = db.Blogs.GroupBy(b => b.Status); var r = q.Select(g => g.Count()).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.IsActive).Where(g => g.Key).Select(g => g.Key).ToList();")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Views).Where(g => g.Key.HasValue).Select(g => g.Key).ToList();")]
    public void SupportedGroupBy_GeneratesCompilingCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating > 3).Select(g => g.Count()).ToList();", "KP2010", "b.Rating > 3")]
    [InlineData("var r = db.Blogs.GroupBy(b => 1).Select(g => g.Count()).ToList();", "KP2010", "1")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Author).Select(g => g.Count()).ToList();", "KP2010", "b.Author")]
    [InlineData("var r = db.Blogs.GroupBy(b => ValueTuple.Create(b.Status, b.Rating)).Select(g => g.Count()).ToList();", "KP2010", "ValueTuple.Create(b.Status, b.Rating)")]
    [InlineData("var r = db.Blogs.GroupBy(b => new { b.Status, Big = b.Rating > 3 }).Select(g => g.Count()).ToList();", "KP2010", "Big = b.Rating > 3")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => g.OrderBy(b => b.Id).First()).ToList();", "KP2011", "g.OrderBy(b => b.Id).First()")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Items = g.ToList() }).ToList();", "KP2011", "g.ToList()")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => g).ToList();", "KP2011", "g")]
    [InlineData("var r = db.Posts.GroupBy(p => p.BlogId).Select(g => g.Max(p => p.Blog.Rating)).ToList();", "KP2011", "p => p.Blog.Rating")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status).Select(g => g.Sum(b => b.Rating * 2)).ToList();", "KP2011", "b => b.Rating * 2")]
    [InlineData("var r = db.Posts.GroupBy(p => p.BlogId).Select(g => g.Count(p => p.Blog.IsActive)).ToList();", "KP2002", "p.Blog.IsActive")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Status, b => b.Rating).Select(g => g.Count(r => r > 1)).ToList();", "KP2011", "g.Count(r => r > 1)")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count(b => b.Rating > g.Key)).ToList();", "KP2011", "g.Key")]
    [InlineData("var r = db.Blogs.GroupBy(b => new { b.Status, b.Rating }).OrderBy(g => g.Key).Select(g => g.Count()).ToList();", "KP2011", "g.Key")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, Label = g.Key + \"!\" }).Where(x => x.Label != \"\").ToList();", "KP2011", "x.Label")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, C = g.Count() }).Take(2).Where(x => x.C > 1).ToList();", "KP1007", "Where")]
    [InlineData("var r = db.Blogs.Select(b => new { b.Status }).GroupBy(x => x.Status).Select(g => g.Count()).ToList();", "KP1007", "GroupBy")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Skip(1).ToList();", "KP1008", "Skip")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).First();", "KP1008", "First")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).ExecuteDelete();", "KP1008", "ExecuteDelete")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).Where(g => g.Count() > 1).ToList();", "KP1008", "ToList")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Name, StringComparer.Ordinal).ToList();", "KP1006", "GroupBy")]
    [InlineData("Expression<Func<Blog, int>> key = b => b.Rating; var r = db.Blogs.GroupBy(key).ToList();", "KP1005", "key")]
    public void UnsupportedGroupBy_ReportsAtTheExpression(string body, string id, string expression)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(expression, run.TextAt(diagnostic));
    }
}
