using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

public sealed class ModelConfigurationDiagnosticsTests
{
    [Theory]
    [InlineData("var limit = 5; modelBuilder.Entity<Blog>().HasQueryFilter(b => b.Rating > limit);", "limit")]
    [InlineData("modelBuilder.Entity<Blog>().HasQueryFilter(b => b.Rating > Compute());", "Compute()")]
    [InlineData("var key = \"x\"; modelBuilder.Entity<Blog>().HasQueryFilter(key, b => b.IsActive);", "key")]
    [InlineData("Expression<Func<Blog, bool>> filter = b => b.IsActive; modelBuilder.Entity<Blog>().HasQueryFilter(filter);", "filter")]
    [InlineData("LambdaExpression filter = (Expression<Func<Blog, bool>>)(b => b.IsActive); modelBuilder.Entity(typeof(Blog)).HasQueryFilter(filter);", "modelBuilder.Entity(typeof(Blog)).HasQueryFilter(filter)")]
    public void UntranslatableFilter_ReportsKP3005(string body, string text)
    {
        var run = GeneratorHost.Run(Source(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3005", diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
        Assert.StartsWith("The query filter cannot be translated at build time: ", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void FilterWithUnsupportedShape_ReportsTranslatorDiagnostic()
    {
        var run = GeneratorHost.Run(Source("modelBuilder.Entity<Blog>().HasQueryFilter(b => b.Name.StartsWith(\"a\"));"));

        Assert.Equal("KP2003", Assert.Single(run.Diagnostics).Id);
    }

    [Fact]
    public void ConfigurationClassFilter_ReadingItsOwnMembers_ReportsKP3005()
    {
        var run = GeneratorHost.Run(Source("") + """

            public sealed class BlogConfiguration : IEntityTypeConfiguration<Blog>
            {
                private readonly int _minimum = 1;

                public void Configure(EntityTypeBuilder<Blog> builder) => builder.HasQueryFilter(b => b.Rating > _minimum);
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3005", diagnostic.Id);
        Assert.Equal("_minimum", run.TextAt(diagnostic));
    }

    [Theory]
    [InlineData("modelBuilder.Entity<Blog>().Property(b => b.Status).HasConversion<string>();", "HasConversion")]
    [InlineData("modelBuilder.Entity<Blog>().Property(b => b.Name).HasConversion(v => v.ToUpperInvariant(), v => v);", "HasConversion")]
    public void ValueConverter_ReportsKP3002(string body, string text)
    {
        var run = GeneratorHost.Run(Source(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3002", diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
    }

    [Fact]
    public void ConventionConverter_ReportsKP3002()
    {
        var run = GeneratorHost.Run(Source("") + """

            public sealed class ConventionDb : DbContext
            {
                protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                    => configurationBuilder.Properties<Status>().HaveConversion<string>();
            }
            """);

        Assert.Equal("HaveConversion", run.TextAt(Assert.Single(run.Diagnostics, d => d.Id == "KP3002")));
    }

    [Theory]
    [InlineData("modelBuilder.Entity<Blog>().OwnsOne(b => b.Author);", "OwnsOne")]
    [InlineData("modelBuilder.Entity<Blog>().ComplexProperty(b => b.Author);", "ComplexProperty")]
    public void OwnedOrComplexMapping_ReportsKP3003(string body, string text)
    {
        var run = GeneratorHost.Run(Source(body));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3003", diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
    }

    [Fact]
    public void JsonColumn_ReportsKP3003ForOwnsAndToJson()
    {
        var run = GeneratorHost.Run(Source("modelBuilder.Entity<Blog>().OwnsOne(b => b.Author, a => a.ToJson());"));

        Assert.Equal(["OwnsOne", "ToJson"], run.Diagnostics.Select(run.TextAt).Order(StringComparer.Ordinal));
        Assert.All(run.Diagnostics, d => Assert.Equal("KP3003", d.Id));
    }

    [Fact]
    public void OwnedAttribute_ReportsKP3003AtClass()
    {
        var run = GeneratorHost.Run(Source("") + """

            [Owned]
            public sealed class Address
            {
                public string City { get; set; } = "";
            }
            """);

        Assert.Equal("Address", run.TextAt(Assert.Single(run.Diagnostics)));
    }

    [Fact]
    public void SupportedConfiguration_ReportsNothingAndGeneratesNoFilters()
    {
        var run = GeneratorHost.Run(Source("modelBuilder.Entity<Blog>().ToTable(\"Blogs\").HasKey(b => b.Id);"));

        Assert.Empty(run.Diagnostics);
        Assert.DoesNotContain(run.Result.GeneratedTrees, t => t.FilePath.EndsWith(QueryFilterEmitter.HintName, StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedMethodNamedLikeEfCore_IsIgnored()
    {
        var run = GeneratorHost.Run(Source("") + """

            public static class Lookalike
            {
                public static void HasConversion(this string value) { }

                public static void Use() => "x".HasConversion();
            }
            """);

        Assert.Empty(run.Diagnostics);
    }

    private static string Source(string onModelCreating)
        => TestSources.Usings + "using Microsoft.EntityFrameworkCore.Metadata.Builders;\n\nnamespace App;\n\n" + TestSources.Model + $$"""
            public sealed class ConfigDb : DbContext
            {
                private static int Compute() => 1;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    {{onModelCreating}}
                }
            }
            """;
}
