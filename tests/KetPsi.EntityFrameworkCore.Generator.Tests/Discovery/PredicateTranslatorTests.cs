using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Model;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Discovery;

public sealed class PredicateTranslatorTests
{
    // Locals the lambdas can capture.
    private const string Locals = """
        var min = 3;
        long total = 5;
        var flag = true;
        string? name = "x";
        List<int> ids = [1, 2];
        int[] array = [1, 2];
        var request = new Request(1, "y");
        """;

    private const string Right = "((E.BinaryExpression)body).Right";
    private const string Left = "((E.BinaryExpression)body).Left";

    [Theory]
    [InlineData("b => b.Rating > 3", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)", Right)]
    [InlineData("b => 3 < b.Rating", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)", Left)]
    [InlineData("b => min >= b.Rating", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.LessThanOrEqual, 0)", Left)]
    [InlineData("b => b.Rating != min", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.NotEqual, 0)", Right)]
    [InlineData("b => b.Status == Status.Published", "new T.ComparisonTemplate(\"Status\", T.ComparisonOperator.Equal, 0)", Right)]
    [InlineData("b => b.Rating > request.Min", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)", Right)]
    [InlineData("b => b.Rating < Limits.Max", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.LessThan, 0)", Right)]
    [InlineData("b => b.Rating > (int)total", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)", Right)]
    [InlineData("b => (b.Views) == (min)", "new T.ComparisonTemplate(\"Views\", T.ComparisonOperator.Equal, 0)", Right)]
    [InlineData("b => b.Name == request.Name!", "new T.ComparisonTemplate(\"Name\", T.ComparisonOperator.Equal, 0)", Right)]

    // default(int?) is not a compile-time constant; its null value turns into IS NULL when the query runs.
    [InlineData("b => b.Views == default", "new T.ComparisonTemplate(\"Views\", T.ComparisonOperator.Equal, 0)", Right)]
    [InlineData("b => b.Name.Equals(name)", "new T.ComparisonTemplate(\"Name\", T.ComparisonOperator.Equal, 0)", "((E.MethodCallExpression)body).Arguments[0]")]
    [InlineData("b => ids.Contains(b.Id)", "new T.InListTemplate(\"Id\", 0)", "((E.MethodCallExpression)body).Object!")]
    [InlineData("b => array.Contains(b.Id)", "new T.InListTemplate(\"Id\", 0)", "((E.MethodCallExpression)body).Arguments[0]")]
    [InlineData("b => Enumerable.Contains(ids, b.Id)", "new T.InListTemplate(\"Id\", 0)", "((E.MethodCallExpression)body).Arguments[0]")]
    public void Translate_Comparison_HasOneValueSlot(string lambda, string template, string path)
    {
        var predicate = TranslateValid(lambda);

        Assert.Equal(template, Shorten(predicate.TemplateCode));
        Assert.Equal([path], predicate.ValuePaths.Select(Shorten));
    }

    [Theory]
    [InlineData("b => b.IsActive", "new T.BooleanPropertyTemplate(\"IsActive\")")]
    [InlineData("b => !b.IsActive", "new T.NotTemplate(new T.BooleanPropertyTemplate(\"IsActive\"))")]
    [InlineData("b => b.Views == null", "new T.NullCheckTemplate(\"Views\", isNull: true)")]
    [InlineData("b => null != b.Views", "new T.NullCheckTemplate(\"Views\", isNull: false)")]
    [InlineData("b => b.Name == null", "new T.NullCheckTemplate(\"Name\", isNull: true)")]
    [InlineData("b => b.Views.HasValue", "new T.NullCheckTemplate(\"Views\", isNull: false)")]
    [InlineData("b => !b.Views.HasValue", "new T.NotTemplate(new T.NullCheckTemplate(\"Views\", isNull: false))")]
    [InlineData("b => b.Name.Equals(null)", "new T.NullCheckTemplate(\"Name\", isNull: true)")]
    public void Translate_WithoutValues_HasNoSlots(string lambda, string template)
    {
        var predicate = TranslateValid(lambda);

        Assert.Equal(template, Shorten(predicate.TemplateCode));
        Assert.Empty(predicate.ValuePaths);
    }

    [Fact]
    public void Translate_LogicalOperators_NestPathsAndNumberSlotsInOrder()
    {
        var predicate = TranslateValid("b => b.IsActive && (b.Rating >= min || !(b.Name == name))");

        Assert.Equal(
            "new T.AndTemplate(new T.BooleanPropertyTemplate(\"IsActive\"), new T.OrTemplate(" +
            "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThanOrEqual, 0), " +
            "new T.NotTemplate(new T.ComparisonTemplate(\"Name\", T.ComparisonOperator.Equal, 1))))",
            Shorten(predicate.TemplateCode));
        Assert.Equal(
            [
                "((E.BinaryExpression)((E.BinaryExpression)((E.BinaryExpression)body).Right).Left).Right",
                "((E.BinaryExpression)((E.UnaryExpression)((E.BinaryExpression)((E.BinaryExpression)body).Right).Right).Operand).Right",
            ],
            predicate.ValuePaths.Select(Shorten));
    }

    [Theory]
    [InlineData("b => b.Author!.Id == 1", "KP2002", "b.Author!.Id")]
    [InlineData("b => b.Author == null", "KP2002", "b.Author")]
    [InlineData("b => b.Name.StartsWith(\"a\")", "KP2003", "b.Name.StartsWith(\"a\")")]
    [InlineData("b => b.Name.Length > 3", "KP2003", "b.Name.Length")]
    [InlineData("b => b.Name.ToUpper() == name", "KP2003", "b.Name.ToUpper()")]
    [InlineData("b => EF.Functions.Like(b.Name, \"a%\")", "KP2004", "EF.Functions.Like(b.Name, \"a%\")")]
    [InlineData("b => b.Posts.Any()", "KP2005", "b.Posts.Any()")]
    [InlineData("b => b.Rating > b.Id", "KP2006", "b.Rating > b.Id")]
    [InlineData("b => b.Rating > Math.Abs(min)", "KP2007", "Math.Abs(min)")]
    [InlineData("b => b.Rating > min + 1", "KP2007", "min + 1")]
    [InlineData("b => b.Rating > ids[0]", "KP2007", "ids[0]")]
    [InlineData("b => true", "KP2001", "true")]
    [InlineData("b => flag && b.IsActive", "KP2001", "flag")]
    [InlineData("b => b.Rating + 1 > 3", "KP2001", "b.Rating + 1")]
    [InlineData("b => b.IsActive ? true : false", "KP2001", "b.IsActive ? true : false")]
    [InlineData("b => min > 3", "KP2001", "min > 3")]
    [InlineData("b => b.Rating > null", "KP2001", "b.Rating > null")]
    public void Translate_Unsupported_ReportsDiagnosticAtConstruct(string lambda, string id, string text)
    {
        var (predicate, diagnostic) = Translate(lambda);

        Assert.Null(predicate);
        Assert.NotNull(diagnostic);
        Assert.Equal(id, diagnostic.Descriptor.Id);
        Assert.Equal(text, diagnostic.MessageArgs[0]);
    }

    [Fact]
    public void Generator_ReportsTranslationDiagnosticAtConstruct()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Where(b => b.Name.StartsWith(\"a\")).AsNoTracking().ToList();"));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2003", diagnostic.Id);
        Assert.Equal("b.Name.StartsWith(\"a\")", run.TextAt(diagnostic));
        Assert.Equal("'b.Name.StartsWith(\"a\")' calls a method or member that cannot be translated to SQL yet", diagnostic.GetMessage());
    }

    [Fact]
    public void Generator_TranslatesPredicatesOfTerminals()
        => Assert.Equal(
            "KP2006",
            Assert.Single(GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Count(b => b.Rating > b.Id);")).Diagnostics).Id);

    private static PredicateModel TranslateValid(string lambda)
    {
        var (predicate, diagnostic) = Translate(lambda);

        Assert.Null(diagnostic);
        return Assert.IsType<PredicateModel>(predicate);
    }

    private static (PredicateModel? Predicate, DiagnosticInfo? Diagnostic) Translate(string lambda)
    {
        var compilation = GeneratorHost.Compile(TestSources.WithBody(Locals + $"\nvar q = db.Blogs.Where({lambda});"));
        var tree = compilation.SyntaxTrees.Single();
        var where = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Where" });
        var syntax = where.ArgumentList.Arguments[0].Expression;

        return PredicateTranslator.Translate((LambdaExpressionSyntax)syntax, compilation.GetSemanticModel(tree), TestContext.Current.CancellationToken);
    }

    private static string Shorten(string code)
        => code
            .Replace("global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.", "T.", StringComparison.Ordinal)
            .Replace("global::System.Linq.Expressions.", "E.", StringComparison.Ordinal);
}
