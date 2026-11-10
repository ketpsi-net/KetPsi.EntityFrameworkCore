using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class SqlQueryDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Database.SqlQuery<int>($\"SELECT Id AS Value FROM Blogs\").ToList();")]
    [InlineData("var min = 1; var r = db.Database.SqlQuery<int>($\"SELECT Id AS Value FROM Blogs\").Where(v => v > min).OrderByDescending(v => v).Take(2).ToList();")]
    [InlineData("var r = await db.Database.SqlQuery<int>($\"SELECT Id AS Value FROM Blogs\").CountAsync(ct);")]
    [InlineData("var r = db.Database.SqlQuery<int>($\"SELECT Id AS Value FROM Blogs\").Sum();")]
    [InlineData("var r = await db.Database.SqlQuery<long>($\"SELECT Id AS Value FROM Blogs\").OrderBy(v => v).FirstAsync(ct);")]
    [InlineData("var r = db.Database.SqlQueryRaw<string>(\"SELECT Name AS Value FROM Blogs WHERE Rating > {0}\", 2).ToList();")]
    [InlineData("var r = db.Database.SqlQuery<int?>($\"SELECT Views AS Value FROM Blogs\").Where(v => v != null).ToList();")]
    [InlineData("var r = db.Database.SqlQuery<Status>($\"SELECT Status AS Value FROM Blogs\").ToList();")]
    [InlineData("var r = db.Database.SqlQuery<Request>($\"SELECT Rating AS Min, Name FROM Blogs\").ToList();")]
    [InlineData("var r = db.Database.SqlQuery<Request>($\"SELECT Rating AS Min, Name FROM Blogs\").Where(x => x.Min > 1 && x.Name != null).OrderBy(x => x.Name).Select(x => x.Name).ToList();")]
    [InlineData("var r = db.Database.SqlQuery<Request>($\"SELECT Rating AS Min, Name FROM Blogs\").Select(x => new { x.Min, Label = x.Name + \"!\" }).ToList();")]
    public void SupportedSqlQuery_GeneratesCompilingCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Theory]
    [InlineData("var r = db.Database.SqlQuery<Blog>($\"SELECT * FROM Blogs\").ToList();", "KP1004", "SqlQuery", "'Author' is a navigation")]
    [InlineData("var r = db.Database.SqlQueryRaw<List<int>>(\"SELECT 1\").ToList();", "KP1004", "SqlQueryRaw", "it must be a scalar")]
    [InlineData("var r = db.Database.SqlQuery<int>($\"SELECT 1 AS Value\").Take(1).Where(v => v > 0).ToList();", "KP1007", "Where", "")]
    public void UnsupportedSqlQuery_ReportsAtTheCall(string body, string id, string text, string reason)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
        Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
