using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class GeneratorDiagnosticsTests
{
    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().Where(b => b.Rating > 3).OrderBy(b => b.Name).ThenByDescending(b => b.Id).Skip(1).Take(2).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderByDescending(b => b.Rating).ThenBy(b => b.Id).ToArray();")]
    [InlineData("var r = await db.Blogs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == 1, ct);")]
    [InlineData("var r = await db.Blogs.AsNoTracking().ToListAsync(ct);")]
    [InlineData("var r = await db.Blogs.AsNoTracking().ToArrayAsync(ct);")]
    [InlineData("var r = await db.Blogs.AsNoTracking().SingleOrDefaultAsync(ct);")]
    [InlineData("var r = await db.Blogs.LongCountAsync(b => b.IsActive, ct);")]
    [InlineData("var r = db.Blogs.IgnoreQueryFilters().Count();")]
    [InlineData("var r = db.Blogs.IgnoreQueryFilters([\"SoftDelete\"]).Any(b => b.IsActive);")]
    [InlineData("var r = db.Blogs.AsNoTracking().WhereIf(ct.CanBeCanceled, b => b.IsActive).First();")]
    [InlineData("var r = db.Blogs.AsNoTracking().Single(b => b.Id == 2);")]
    [InlineData("IQueryable<Blog> q = db.Blogs.AsNoTracking(); if (ct.CanBeCanceled) { q = q.Where(b => b.IsActive); } var r = q.ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().AsEnumerable().Where(b => b.IsActive).ToList();")]
    public void SupportedQuery_ReportsNothing(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
    }

    [Theory]
    [InlineData("var r = db.Blogs.Select(b => b.Id).ToList();", "Select")]
    [InlineData("var r = db.Blogs.GroupBy(b => b.Rating).ToList();", "GroupBy")]
    [InlineData("var r = db.Blogs.Distinct().ToList();", "Distinct")]
    [InlineData("var r = db.Blogs.Join(db.Posts, b => b.Id, p => p.BlogId, (b, p) => b).ToList();", "Join")]
    [InlineData("var r = db.Blogs.Max(b => b.Rating);", "Max")]
    [InlineData("var r = db.Blogs.Last();", "Last")]
    [InlineData("var r = db.Blogs.AsNoTrackingWithIdentityResolution().ToList();", "AsNoTrackingWithIdentityResolution")]
    [InlineData("var r = db.Blogs.TagWith(\"x\").ToList();", "TagWith")]
    [InlineData("var r = db.Blogs.Where(b => b.Id == 1).ExecuteDelete();", "ExecuteDelete")]
    [InlineData("var r = db.Blogs.TemporalAll().ToList();", "TemporalAll")]
    [InlineData("var r = db.Blogs.Find(1);", "Find")]
    [InlineData("var r = await db.Blogs.FindAsync(1);", "FindAsync")]
    [InlineData("var r = db.Blogs.AsAsyncEnumerable();", "AsAsyncEnumerable")]
    [InlineData("var r = db.Blogs.AsEnumerable().ToList();", "AsEnumerable")]
    [InlineData("var r = db.Blogs.AsSplitQuery().ToList();", "AsSplitQuery")]
    [InlineData("var r = await db.Blogs.MaxAsync(b => b.Rating, ct);", "MaxAsync")]
    [InlineData("var r = Queryable.Select(db.Blogs, b => b.Id).ToList();", "Select")]
    public void UnsupportedOperator_ReportsKP1001AtOperatorName(string body, string operatorName)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(operatorName, run.TextAt(diagnostic));
        Assert.Equal($"The query operator '{operatorName}' is not supported by KetPsi.EntityFrameworkCore", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("var r = db.Blogs.Include(b => b.Posts).AsNoTracking().ToList();", "Include")]
    [InlineData("var r = db.Blogs.Include(b => b.Posts).ThenInclude(p => p.BlogId).AsNoTracking().ToList();", "ThenInclude")]
    public void Include_ReportsKP1002(string body, string operatorName)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics, d => run.TextAt(d) == operatorName);
        Assert.Equal("KP1002", diagnostic.Id);
        Assert.Equal($"'{operatorName}' is not supported yet. Load related entities with separate queries.", diagnostic.GetMessage());
    }

    [Fact]
    public void AsTracking_ReportsKP1003()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsTracking().ToList();"));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1003", diagnostic.Id);
        Assert.Equal("AsTracking", run.TextAt(diagnostic));
    }

    [Theory]
    [InlineData("var r = db.Blogs.FromSqlRaw(\"SELECT * FROM Blogs\").ToList();", "FromSqlRaw")]
    [InlineData("var r = db.Blogs.FromSql($\"SELECT * FROM Blogs\").ToList();", "FromSql")]
    [InlineData("var r = db.Blogs.FromSqlInterpolated($\"SELECT * FROM Blogs\").ToList();", "FromSqlInterpolated")]
    public void RawSql_ReportsKP1004(string body, string operatorName)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1004", diagnostic.Id);
        Assert.Equal(operatorName, run.TextAt(diagnostic));
        Assert.Equal($"Raw SQL ('{operatorName}') is not supported by KetPsi.EntityFrameworkCore", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("Expression<Func<Blog, bool>> p = b => b.IsActive; var r = db.Blogs.AsNoTracking().Where(p).ToList();", "p", "predicate", "Where")]
    [InlineData("Expression<Func<Blog, int>> k = b => b.Id; var r = db.Blogs.AsNoTracking().OrderBy(k).ToList();", "k", "keySelector", "OrderBy")]
    [InlineData("Expression<Func<Blog, bool>> p = b => b.IsActive; var r = db.Blogs.Count(p);", "p", "predicate", "Count")]
    [InlineData("Expression<Func<Blog, bool>> p = b => b.IsActive; var r = await db.Blogs.AnyAsync(p, ct);", "p", "predicate", "AnyAsync")]
    [InlineData("Expression<Func<Blog, bool>> p = b => b.IsActive; var r = db.Blogs.AsNoTracking().WhereIf(true, p).ToList();", "p", "predicate", "WhereIf")]
    public void PredicateNotLambda_ReportsKP1005AtArgument(string body, string argumentText, string parameterName, string operatorName)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1005", diagnostic.Id);
        Assert.Equal(argumentText, run.TextAt(diagnostic));
        Assert.Equal(
            $"The '{parameterName}' argument of '{operatorName}' must be a lambda expression written at the call site, not a variable or a method",
            diagnostic.GetMessage());
    }

    [Fact]
    public void ParenthesizedLambda_IsAccepted()
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsNoTracking().Where((b => b.IsActive)).ToList();")).Diagnostics);

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().Where((b, i) => i > 1).ToList();", "Where", "predicates that take the element index are not supported")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name, StringComparer.Ordinal).ToList();", "OrderBy", "custom comparers are not supported")]
    [InlineData("var r = db.Blogs.AsNoTracking().Take(1..3).ToList();", "Take", "only an int count is supported")]
    [InlineData("var r = db.Blogs.FirstOrDefault(new Blog());", "FirstOrDefault", "overloads with a default value are not supported")]
    public void UnsupportedOverload_ReportsKP1006(string body, string operatorName, string reason)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1006", diagnostic.Id);
        Assert.Equal(operatorName, run.TextAt(diagnostic));
        Assert.Equal($"This overload of '{operatorName}' is not supported: {reason}", diagnostic.GetMessage());
    }

    [Fact]
    public void SeveralUnsupportedOperators_ReportEach()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Include(b => b.Posts).Select(b => b.Id).ToList();"));

        Assert.Equal(["KP1002", "KP1001"], run.Diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(d => d.Id));
    }

    [Theory]
    [InlineData("var r = new List<int>().AsQueryable().Select(x => x).ToList();")]
    [InlineData("var r = new List<Blog>().Where(b => b.IsActive).Select(b => b.Id).ToList();")]
    public void NonEntityQuery_IsIgnored(string body)
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody(body)).Diagnostics);

    // Entity queries are recognized by element type, because a query composed over several statements has no
    // visible root. An in-memory IQueryable of an entity type is therefore checked too: supported operators fall
    // back to LINQ to Objects at runtime, unsupported ones are reported.
    [Fact]
    public void InMemoryQueryOfEntityType_IsCheckedLikeEntityQuery()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = new List<Blog>().AsQueryable().Select(b => b.Id).ToList();"));

        Assert.Equal("KP1001", Assert.Single(run.Diagnostics).Id);
    }

    [Fact]
    public void GenericHelper_IsNotAnalyzed()
    {
        var source = TestSources.Usings + "namespace App;\n\n" + TestSources.Model + """
            public static class Paging
            {
                public static IQueryable<T> Page<T>(IQueryable<T> query) => query.Skip(10).Take(10).Distinct();
            }
            """;

        Assert.Empty(GeneratorHost.Run(source).Diagnostics);
    }

    [Fact]
    public void InterceptorsDisabled_WithSupportedOperators_ReportsKP0002Once()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsNoTracking().Where(b => b.IsActive).ToList();"), interceptorsEnabled: false);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP0002", diagnostic.Id);
        Assert.Equal(Location.None, diagnostic.Location);
        Assert.Contains(QueryInterceptorGenerator.GeneratedNamespace, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void InterceptorsDisabled_WithoutEntityQueries_ReportsNothing()
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody("var r = 1;"), interceptorsEnabled: false).Diagnostics);

    [Fact]
    public void InterceptorsDisabled_OnlyUnsupportedOperators_ReportsOnlyThem()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Select(b => b.Id);"), interceptorsEnabled: false);

        Assert.Equal("KP1001", Assert.Single(run.Diagnostics).Id);
    }
}
