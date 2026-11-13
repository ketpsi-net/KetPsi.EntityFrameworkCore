using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

/// <summary>String methods and <c>EF.Functions.Like</c> in predicates and order keys, as EF Core translates them.</summary>
public sealed class StringMethodEmitTests
{
    private const string T = "global::KetPsi.EntityFrameworkCore.Infrastructure.Templates.";

    [Theory]
    [InlineData("b => b.Name.Contains(\"a\")", "StringMatchTemplate(T.StringMatch.Contains, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.StartsWith(name)", "StringMatchTemplate(T.StringMatch.StartsWith, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0))")]
    [InlineData("b => !b.Name.EndsWith('x')", "NotTemplate(new T.StringMatchTemplate(T.StringMatch.EndsWith, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0)))")]
    [InlineData("b => b.Name.Contains(b.Author!.Id.ToString())", "StringMatchTemplate(T.StringMatch.Contains, new T.PropertyValueTemplate(\"Name\"), new T.FunctionValueTemplate(T.ValueFunction.ToString, new T.PropertyValueTemplate(\"Author.Id\")))")]
    [InlineData("b => string.IsNullOrEmpty(b.Name)", "NullOrEmptyTemplate(new T.PropertyValueTemplate(\"Name\"), orWhiteSpace: false)")]
    [InlineData("b => !string.IsNullOrWhiteSpace(b.Name)", "NotTemplate(new T.NullOrEmptyTemplate(new T.PropertyValueTemplate(\"Name\"), orWhiteSpace: true))")]
    [InlineData("b => EF.Functions.Like(b.Name, \"a%\")", "LikeTemplate(new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0))")]
    [InlineData("b => EF.Functions.Like(b.Name, name + \"%\", \"!\")", "LikeTemplate(new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0), new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Name.Length > 3", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.Length, new T.PropertyValueTemplate(\"Name\")), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.ToUpper() == name", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.ToUpper, new T.PropertyValueTemplate(\"Name\")), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.Trim().ToLower() == name", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.ToLower, new T.FunctionValueTemplate(T.ValueFunction.Trim, new T.PropertyValueTemplate(\"Name\"))), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.Substring(1, 2) == name", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.Substring, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0), new T.SlotValueTemplate(1)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(2))")]
    [InlineData("b => b.Name.Replace(\"a\", \"b\") == name", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.Replace, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0), new T.SlotValueTemplate(1)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(2))")]
    [InlineData("b => b.Name.IndexOf('a') == 1", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.IndexOf, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(1))")]
    [InlineData("b => b.Views.ToString() == name", "ValueComparisonTemplate(new T.FunctionValueTemplate(T.ValueFunction.ToString, new T.PropertyValueTemplate(\"Views\")), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => string.Concat(b.Name, name) == name", "ValueComparisonTemplate(new T.BinaryValueTemplate(T.ValueOperator.Concat, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0)), T.ComparisonOperator.Equal, new T.SlotValueTemplate(1))")]
    [InlineData("b => string.Compare(b.Name, name) > 0", "ValueComparisonTemplate(new T.PropertyValueTemplate(\"Name\"), T.ComparisonOperator.GreaterThan, new T.SlotValueTemplate(0))")]
    [InlineData("b => 0 >= b.Name.CompareTo(name)", "ValueComparisonTemplate(new T.PropertyValueTemplate(\"Name\"), T.ComparisonOperator.LessThanOrEqual, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.Equals(b.Name.ToUpper())", "ValueComparisonTemplate(new T.PropertyValueTemplate(\"Name\"), T.ComparisonOperator.Equal, new T.FunctionValueTemplate(T.ValueFunction.ToUpper, new T.PropertyValueTemplate(\"Name\")))")]
    [InlineData("b => string.Equals(b.Name, name)", "ValueComparisonTemplate(new T.PropertyValueTemplate(\"Name\"), T.ComparisonOperator.Equal, new T.SlotValueTemplate(0))")]
    [InlineData("b => b.Name.StartsWith(\"a\") == b.IsActive", "ValueComparisonTemplate(new T.PredicateValueTemplate(new T.StringMatchTemplate(T.StringMatch.StartsWith, new T.PropertyValueTemplate(\"Name\"), new T.SlotValueTemplate(0))), T.ComparisonOperator.Equal, new T.PropertyValueTemplate(\"IsActive\"))")]
    public void StringMethod_IsTranslatedAsEfCoreTranslatesIt(string predicate, string template)
    {
        var run = GeneratorHost.Run(TestSources.WithBody($"var name = \"x\"; _ = name; var r = db.Blogs.AsNoTracking().Where({predicate}).ToList();"));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        Assert.Contains("new T." + template, run.Generated(InterceptorEmitter.HintName).Replace(T, "T.", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name.Length).ThenByDescending(b => b.Name.ToLower()).ToList();")]
    [InlineData("var r = db.Blogs.AsNoTracking().OrderBy(b => b.Name.Substring(1, 1)).ToList();")]
    [InlineData("var r = db.Posts.AsNoTracking().Where(p => p.Blog.Name.StartsWith(\"a\") && p.Blog.Name.Length > 2).ToList();")]
    [InlineData("var r = db.Blogs.Where(b => b.Id > 0).Select(b => new { b.Id, b.Name }).Where(x => x.Name.Contains(\"a\")).ToList();")]
    [InlineData("var p = \"a\"; var r = await db.Blogs.CountAsync(b => b.Name.Contains(p.ToUpper()) || EF.Functions.Like(b.Name, p + \"%\"), ct);")]
    public void StringMethodsInOtherPlaces_CompileInTheGeneratedCode(string body)
    {
        var run = GeneratorHost.Run(TestSources.WithBody(body));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void QueryFilterWithAStringMethod_IsTranslated()
    {
        var run = GeneratorHost.Run(TestSources.Usings + "namespace App;\n\n" + TestSources.Model.Replace(
            "public DbSet<Post> Posts => Set<Post>();",
            "public DbSet<Post> Posts => Set<Post>();\n    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Blog>().HasQueryFilter(x => !x.Name.StartsWith(\"_\"));",
            StringComparison.Ordinal));

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        Assert.Contains("StringMatchTemplate", run.Generated(QueryFilterEmitter.HintName), StringComparison.Ordinal);
    }
}
