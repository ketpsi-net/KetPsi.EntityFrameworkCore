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
        var anon = new { Limit = 4 };
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
        Assert.Equal([$"C.Read({path})"], predicate.ValueCodes.Select(Shorten));
        Assert.True(predicate.ReadsBody);
        Assert.False(predicate.ReadsCaptures);
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
        Assert.Empty(predicate.ValueCodes);
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
                "C.Read(((E.BinaryExpression)((E.BinaryExpression)((E.BinaryExpression)body).Right).Left).Right)",
                "C.Read(((E.BinaryExpression)((E.UnaryExpression)((E.BinaryExpression)((E.BinaryExpression)body).Right).Right).Operand).Right)",
            ],
            predicate.ValueCodes.Select(Shorten));
    }

    // A value that does not read the row is computed in C# from the captured variables, numbered as ReadAll lists them.
    [Theory]
    [InlineData("b => b.Rating > min + 1", "(((int)captures[0]!) + 1)")]
    [InlineData("b => b.Rating > Math.Abs(min)", "(global::System.Math.Abs(((int)captures[0]!)))")]
    [InlineData("b => b.Rating > ids[0]", "(((global::System.Collections.Generic.List<int>)captures[0]!)[0])")]
    [InlineData("b => b.Name == name + \"!\"", "(((string)captures[0]!) + \"!\")")]
    [InlineData("b => b.Rating > Limits.Max * 2", "(((int)captures[0]!) * 2)")]
    [InlineData("b => b.Status == (min > 2 ? Status.Draft : Status.Published)", "((((int)captures[0]!) > 2 ? ((global::App.Status)(0)) : ((global::App.Status)(1))))")]
    [InlineData("b => b.Name == request.Name!.ToUpper()", "(((global::App.Request)captures[0]!).Name!.ToUpper())")]
    [InlineData("b => b.Name == string.Concat(name, name)", "(string.Concat(((string)captures[0]!), ((string)captures[1]!)))")]
    public void Translate_ComputedValue_IsWrittenInCSharp(string lambda, string code)
    {
        var predicate = TranslateValid(lambda);

        Assert.Equal(code, Shorten(Assert.Single(predicate.ValueCodes)));
        Assert.True(predicate.ReadsCaptures);
        Assert.False(predicate.ReadsBody);
    }

    [Fact]
    public void Translate_CapturesAreNumberedInSourceOrder_AcrossTheWholeLambda()
    {
        var predicate = TranslateValid("b => b.Rating > min && b.Views == min + (int)total && b.Name == name");

        Assert.Equal(
            [
                "C.Read(((E.BinaryExpression)((E.BinaryExpression)((E.BinaryExpression)body).Left).Left).Right)",
                "(((int)captures[1]!) + (int)((long)captures[2]!))",
                "C.Read(((E.BinaryExpression)((E.BinaryExpression)body).Right).Right)",
            ],
            predicate.ValueCodes.Select(Shorten));
    }

    [Theory]
    [InlineData("b => b.Rating > b.Id", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"Rating\"), T.ComparisonOperator.GreaterThan, new T.PropertyValueTemplate(\"Id\"))")]
    [InlineData("b => b.Views == b.Rating", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"Views\"), T.ComparisonOperator.Equal, new T.PropertyValueTemplate(\"Rating\"))")]
    [InlineData("b => b.Price > b.Rating", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"Price\"), T.ComparisonOperator.GreaterThan, new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Rating\"), typeof(decimal)))")]
    [InlineData("b => b.Rating + 1 > 3", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Add, new T.PropertyValueTemplate(\"Rating\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(1))")]
    [InlineData("b => (b.Rating - min) % 2 == 0", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Modulo, new T.BinaryValueTemplate(T.ValueOperator.Subtract, new T.PropertyValueTemplate(\"Rating\"), new T.SlotValueTemplate(0)), new T.SlotValueTemplate(1)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(2))")]
    [InlineData("b => -b.Rating < 0", "new T.ValueComparisonTemplate(new T.UnaryValueTemplate(T.UnaryValueOperator.Negate, new T.PropertyValueTemplate(\"Rating\")), T.ComparisonOperator.LessThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => (b.Rating & 1) == 1", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.BitwiseAnd, new T.PropertyValueTemplate(\"Rating\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Price * b.Rating > 100", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Multiply, new T.PropertyValueTemplate(\"Price\"), new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Rating\"), typeof(decimal))), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => (long)b.Rating * b.Rating > total", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Multiply, new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Rating\"), typeof(long)), new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Rating\"), typeof(long))), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => (int)b.Status == 1", "new T.ValueComparisonTemplate(new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Status\"), typeof(int)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name + \"!\" == name", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Concat, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Name + b.Rating == name", "new T.ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Concat, new T.PropertyValueTemplate(\"Name\"), new T.ConvertValueTemplate(new T.PropertyValueTemplate(\"Rating\"), typeof(string))), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => (b.Views ?? 0) > 3", "new T.ValueComparisonTemplate(new T.CoalesceValueTemplate(new T.PropertyValueTemplate(\"Views\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Views.GetValueOrDefault(5) == 5", "new T.ValueComparisonTemplate(new T.CoalesceValueTemplate(new T.PropertyValueTemplate(\"Views\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Views!.Value > 3", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"Views\"), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => (b.IsActive ? b.Price : 0) > 5", "new T.ValueComparisonTemplate(new T.ConditionalValueTemplate(new T.BooleanPropertyTemplate(\"IsActive\"), new T.PropertyValueTemplate(\"Price\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(1))")]
    [InlineData("b => (b.Rating > 5) == b.IsActive", "new T.ValueComparisonTemplate(new T.PredicateValueTemplate(new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)), T.ComparisonOperator.Equal, new T.PropertyValueTemplate(\"IsActive\"))")]
    [InlineData("b => b.IsActive == (b.Views == null)", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"IsActive\"), T.ComparisonOperator.Equal, new T.PredicateValueTemplate(new T.NullCheckTemplate(\"Views\", isNull: true)))")]
    public void Translate_ComputedComparison_ComparesValueTemplates(string lambda, string template)
        => Assert.Equal(template, Shorten(TranslateValid(lambda).TemplateCode));

    [Theory]
    [InlineData("b => true", "new T.BooleanValueTemplate(new T.SlotValueTemplate(0))")]
    [InlineData("b => flag && b.IsActive", "new T.AndTemplate(new T.BooleanValueTemplate(new T.SlotValueTemplate(0)), new T.BooleanPropertyTemplate(\"IsActive\"))")]
    [InlineData("b => min > 3", "new T.BooleanValueTemplate(new T.SlotValueTemplate(0))")]
    [InlineData("b => b.IsActive ? true : false", "new T.BooleanValueTemplate(new T.ConditionalValueTemplate(new T.BooleanPropertyTemplate(\"IsActive\"), new T.SlotValueTemplate(0), new T.SlotValueTemplate(1)))")]
    [InlineData("b => b.IsActive ^ (b.Rating > 3)", "new T.BooleanValueTemplate(new T.BinaryValueTemplate(T.ValueOperator.ExclusiveOr, new T.PropertyValueTemplate(\"IsActive\"), new T.PredicateValueTemplate(new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0))))")]
    [InlineData("b => b.IsActive & b.Rating > 3", "new T.AndTemplate(new T.BooleanPropertyTemplate(\"IsActive\"), new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0))")]
    [InlineData("b => b.IsActive | b.Rating > 3", "new T.OrTemplate(new T.BooleanPropertyTemplate(\"IsActive\"), new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0))")]
    public void Translate_ComputedBoolean_IsTestedAsAValue(string lambda, string template)
        => Assert.Equal(template, Shorten(TranslateValid(lambda).TemplateCode));

    [Theory]
    [InlineData("b => b.Author!.Id == 1", "KP2002", "b.Author!.Id")]
    [InlineData("b => b.Author == null", "KP2002", "b.Author")]
    [InlineData("b => b.Name.StartsWith(\"a\")", "KP2003", "b.Name.StartsWith(\"a\")")]
    [InlineData("b => b.Name.Length > 3", "KP2003", "b.Name.Length")]
    [InlineData("b => b.Name.ToUpper() == name", "KP2003", "b.Name.ToUpper()")]
    [InlineData("b => EF.Functions.Like(b.Name, \"a%\")", "KP2004", "EF.Functions.Like(b.Name, \"a%\")")]
    [InlineData("b => b.Posts.Any()", "KP2005", "b.Posts.Any()")]
    [InlineData("b => b.Rating > anon.Limit + 1", "KP2007", "anon")]
    [InlineData("b => b.Rating > ids.Count(x => x > 1)", "KP2007", "x => x > 1")]
    [InlineData("b => b.Rating > null", "KP2001", "b.Rating > null")]
    [InlineData("b => (b.Rating << 1) > 3", "KP2001", "b.Rating << 1")]
    [InlineData("b => b.Name + b.Status == name", "KP2001", "b.Status")]
    [InlineData("b => b.Name + b.Price == name", "KP2001", "b.Price")]
    [InlineData("b => b.Views.GetValueOrDefault() == 0", "KP2003", "b.Views.GetValueOrDefault()")]
    public void Translate_Unsupported_ReportsDiagnosticAtConstruct(string lambda, string id, string text)
    {
        var (predicate, diagnostic) = Translate(lambda);

        Assert.Null(predicate);
        Assert.NotNull(diagnostic);
        Assert.Equal(id, diagnostic.Descriptor.Id);
        Assert.Equal(text, diagnostic.MessageArgs[0]);
    }

    [Theory]
    [InlineData("b => b.Author!.Id == min", "new T.ComparisonTemplate(\"Author.Id\", T.ComparisonOperator.Equal, 0)")]
    [InlineData("b => b.Author == null", "new T.NullCheckTemplate(\"Author\", isNull: true)")]
    [InlineData("b => null != b.Author", "new T.NullCheckTemplate(\"Author\", isNull: false)")]
    [InlineData("b => !(b.Author != null) || ids.Contains(b.Author!.Id)", "new T.OrTemplate(new T.NotTemplate(new T.NullCheckTemplate(\"Author\", isNull: false)), new T.InListTemplate(\"Author.Id\", 0))")]
    [InlineData("b => b.Rating > 1", "new T.ComparisonTemplate(\"Rating\", T.ComparisonOperator.GreaterThan, 0)")]
    [InlineData("b => b.Author!.Id > b.Id", "new T.ValueComparisonTemplate(new T.PropertyValueTemplate(\"Author.Id\"), T.ComparisonOperator.GreaterThan, new T.PropertyValueTemplate(\"Id\"))")]
    [InlineData("b => (b.Author == null ? 0 : b.Rating) > 1", "new T.ValueComparisonTemplate(new T.ConditionalValueTemplate(new T.NullCheckTemplate(\"Author\", isNull: true), new T.SlotValueTemplate(0), new T.PropertyValueTemplate(\"Rating\")), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(1))")]
    public void Translate_WithNavigations_NamesPropertyPaths(string lambda, string template)
    {
        var (predicate, diagnostic) = Translate(lambda, allowNavigations: true);

        Assert.Null(diagnostic);
        Assert.Equal(template, Shorten(predicate!.TemplateCode));
    }

    [Theory]
    [InlineData("b => b.Author == b.Author", "KP2002", "b.Author")]
    [InlineData("b => b.Posts.Any()", "KP2005", "b.Posts.Any()")]
    [InlineData("b => b.Posts.Count + 1 > 1", "KP2005", "b.Posts.Count")]
    [InlineData("b => b.Posts.Count > 1", "KP2005", "b.Posts.Count")]
    [InlineData("b => b.Posts == null", "KP2005", "b.Posts")]
    [InlineData("b => b.Name.Length > 3", "KP2003", "b.Name.Length")]
    public void Translate_WithNavigations_StillReportsTheRest(string lambda, string id, string text)
    {
        var (predicate, diagnostic) = Translate(lambda, allowNavigations: true);

        Assert.Null(predicate);
        Assert.Equal(id, diagnostic!.Descriptor.Id);
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
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var min = 1; var r = db.Blogs.Count(b => b.Rating > b.Id + min);"));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    private static PredicateModel TranslateValid(string lambda)
    {
        var (predicate, diagnostic) = Translate(lambda);

        Assert.Null(diagnostic);
        return Assert.IsType<PredicateModel>(predicate);
    }

    private static (PredicateModel? Predicate, DiagnosticInfo? Diagnostic) Translate(string lambda, bool allowNavigations = false)
    {
        var compilation = GeneratorHost.Compile(TestSources.WithBody(Locals + $"\nvar q = db.Blogs.Where({lambda});"));
        var tree = compilation.SyntaxTrees.Single();
        var where = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Where" });
        var syntax = where.ArgumentList.Arguments[0].Expression;

        return PredicateTranslator.Translate(
            (LambdaExpressionSyntax)syntax, compilation.GetSemanticModel(tree), TestContext.Current.CancellationToken, allowNavigations: allowNavigations);
    }

    private static string Shorten(string code)
        => code
            .Replace("global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.", "T.", StringComparison.Ordinal)
            .Replace("global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues.", "C.", StringComparison.Ordinal)
            .Replace("global::System.Linq.Expressions.", "E.", StringComparison.Ordinal);
}
