using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class DecimalOperatorEmitTests
{
    private const string Dependency =
        "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(" +
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods, typeof(decimal))]";

    [Fact]
    public void DecimalPredicate_KeepsDecimalMethodsForReflection()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsNoTracking().Where(b => b.Price > 10m).ToList();"));

        var generated = run.Generated(InterceptorEmitter.HintName);

        Assert.Equal(1, Count(generated, Dependency));
        Assert.Contains(Dependency + "\n        public static global::System.Linq.IQueryable<global::App.Blog> Where", generated.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void OtherPredicates_DoNotKeepDecimalMethods()
    {
        var run = GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.AsNoTracking().Where(b => b.Rating > 1).OrderBy(b => b.Price).ToList();"));

        Assert.DoesNotContain("DynamicDependency", run.Generated(InterceptorEmitter.HintName), StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
