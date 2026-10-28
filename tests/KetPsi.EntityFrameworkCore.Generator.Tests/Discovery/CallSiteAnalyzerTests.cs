using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Model;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Discovery;

public sealed class CallSiteAnalyzerTests
{
    // Operator kinds are internal and xUnit theory methods are public, so the expected kind is passed by name.
    [Theory]
    [InlineData("db.Blogs.Where(b => b.IsActive)", "Where", nameof(OperatorKind.Where), false, false)]
    [InlineData("db.Blogs.WhereIf(true, b => b.IsActive)", "WhereIf", nameof(OperatorKind.WhereIf), false, false)]
    [InlineData("db.Blogs.OrderBy(b => b.Id)", "OrderBy", nameof(OperatorKind.OrderBy), false, false)]
    [InlineData("db.Blogs.OrderByDescending(b => b.Id)", "OrderByDescending", nameof(OperatorKind.OrderByDescending), false, false)]
    [InlineData("db.Blogs.OrderBy(b => b.Id).ThenBy(b => b.Name)", "ThenBy", nameof(OperatorKind.ThenBy), false, false)]
    [InlineData("db.Blogs.OrderBy(b => b.Id).ThenByDescending(b => b.Name)", "ThenByDescending", nameof(OperatorKind.ThenByDescending), false, false)]
    [InlineData("db.Blogs.Skip(1)", "Skip", nameof(OperatorKind.Skip), false, false)]
    [InlineData("db.Blogs.Take(1)", "Take", nameof(OperatorKind.Take), false, false)]
    [InlineData("db.Blogs.AsNoTracking()", "AsNoTracking", nameof(OperatorKind.AsNoTracking), false, false)]
    [InlineData("db.Blogs.AsNoTrackingWithIdentityResolution()", "AsNoTrackingWithIdentityResolution", nameof(OperatorKind.AsNoTrackingWithIdentityResolution), false, false)]
    [InlineData("db.Blogs.AsTracking()", "AsTracking", nameof(OperatorKind.AsTracking), false, false)]
    [InlineData("db.Blogs.AsTracking(QueryTrackingBehavior.NoTracking)", "AsTracking", nameof(OperatorKind.AsTracking), false, false)]
    [InlineData("db.Blogs.IgnoreQueryFilters()", "IgnoreQueryFilters", nameof(OperatorKind.IgnoreQueryFilters), false, false)]
    [InlineData("db.Blogs.ToList()", "ToList", nameof(OperatorKind.ToList), false, false)]
    [InlineData("db.Blogs.ToArray()", "ToArray", nameof(OperatorKind.ToArray), false, false)]
    [InlineData("db.Blogs.ToListAsync(ct)", "ToListAsync", nameof(OperatorKind.ToList), true, false)]
    [InlineData("db.Blogs.ToArrayAsync(ct)", "ToArrayAsync", nameof(OperatorKind.ToArray), true, false)]
    [InlineData("db.Blogs.First()", "First", nameof(OperatorKind.First), false, false)]
    [InlineData("db.Blogs.First(b => b.IsActive)", "First", nameof(OperatorKind.First), false, true)]
    [InlineData("db.Blogs.FirstOrDefaultAsync(ct)", "FirstOrDefaultAsync", nameof(OperatorKind.FirstOrDefault), true, false)]
    [InlineData("db.Blogs.FirstOrDefaultAsync(b => b.IsActive, ct)", "FirstOrDefaultAsync", nameof(OperatorKind.FirstOrDefault), true, true)]
    [InlineData("db.Blogs.Single()", "Single", nameof(OperatorKind.Single), false, false)]
    [InlineData("db.Blogs.SingleOrDefault(b => b.IsActive)", "SingleOrDefault", nameof(OperatorKind.SingleOrDefault), false, true)]
    [InlineData("db.Blogs.Count()", "Count", nameof(OperatorKind.Count), false, false)]
    [InlineData("db.Blogs.CountAsync(b => b.IsActive, ct)", "CountAsync", nameof(OperatorKind.Count), true, true)]
    [InlineData("db.Blogs.LongCount()", "LongCount", nameof(OperatorKind.LongCount), false, false)]
    [InlineData("db.Blogs.Any(b => b.IsActive)", "Any", nameof(OperatorKind.Any), false, true)]
    [InlineData("db.Blogs.AnyAsync(ct)", "AnyAsync", nameof(OperatorKind.Any), true, false)]
    public void Analyze_SupportedOperator_ClassifiesIt(string expression, string operatorName, string kind, bool isAsync, bool hasPredicate)
    {
        var site = AnalyzeSingle(expression, operatorName);

        Assert.NotNull(site);
        Assert.Equal("global::App.Blog", site.ElementType);
        Assert.Equal(Enum.Parse<OperatorKind>(kind), site.Operator);
        Assert.Equal(isAsync, site.IsAsync);
        Assert.Equal(hasPredicate, site.HasPredicate);
        Assert.Null(site.Diagnostic);
    }

    [Fact]
    public void Analyze_NotAnOperator_ReturnsNull()
        => Assert.Null(AnalyzeSingle("db.Blogs.GetHashCode()", "GetHashCode"));

    [Fact]
    public void Analyze_EnumerableOnInMemorySequence_ReturnsNull()
        => Assert.Null(AnalyzeSingle("new List<Blog>().Where(b => b.IsActive)", "Where"));

    [Fact]
    public void Analyze_UnsupportedOperator_HasDiagnosticAndNoOperator()
    {
        var site = AnalyzeSingle("db.Blogs.Select(b => b.Id)", "Select");

        Assert.NotNull(site);
        Assert.Null(site.Operator);
        Assert.Equal("KP1001", site.Diagnostic?.Descriptor.Id);
    }

    [Fact]
    public void InternalError_ReportsKP0001ForAnyQuery()
    {
        var invocation = SyntaxFactory.ParseExpression("db.Blogs.Where(b => true)");

        var site = CallSiteAnalyzer.InternalError((InvocationExpressionSyntax)invocation, new InvalidOperationException("boom"));

        Assert.Equal(CallSiteAnalyzer.UnknownElementType, site.ElementType);
        Assert.Null(site.Operator);
        Assert.Equal("KP0001", site.Diagnostic?.Descriptor.Id);
        Assert.Equal(
            "KetPsi.EntityFrameworkCore.Generator failed while analyzing this call: boom",
            site.Diagnostic!.ToDiagnostic().GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Where", true)]
    [InlineData("WhereIf", true)]
    [InlineData("ToListAsync", true)]
    [InlineData("GetHashCode", false)]
    [InlineData("Foo", false)]
    public void IsCandidate_FiltersByOperatorName(string name, bool expected)
        => Assert.Equal(expected, CallSiteAnalyzer.IsCandidate(SyntaxFactory.ParseExpression($"q.{name}()"), TestContext.Current.CancellationToken));

    [Fact]
    public void IsCandidate_NonMemberInvocation_IsFalse()
        => Assert.False(CallSiteAnalyzer.IsCandidate(SyntaxFactory.ParseExpression("Where()"), TestContext.Current.CancellationToken));

    private static CallSite? AnalyzeSingle(string expression, string operatorName)
    {
        var compilation = GeneratorHost.Compile(TestSources.WithBody($"var r = {expression};"));
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(i => i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText == operatorName);

        return CallSiteAnalyzer.Analyze(invocation, compilation.GetSemanticModel(tree), TestContext.Current.CancellationToken);
    }
}
