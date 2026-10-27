using System.Collections.Immutable;
using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Model;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Discovery;

public sealed class EntityDiscoveryTests
{
    private const string Entities = """
        public sealed class Blog { public int Id { get; set; } }
        public sealed class Post { public int Id { get; set; } }
        public sealed class Tag { public int Id { get; set; } }
        """;

    [Fact]
    public void FromSymbols_DbSetProperties_AreEntities()
    {
        var compilation = GeneratorHost.Compile(TestSources.Usings + "namespace App;\n" + Entities + """
            public sealed class AppDb : DbContext
            {
                public DbSet<Blog> Blogs => Set<Blog>();
                public DbSet<Post> Posts { get; set; } = null!;
            }
            """);

        Assert.Equal(["global::App.Blog", "global::App.Post"], EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_DbSetOnBaseContext_IsEntity()
    {
        var compilation = GeneratorHost.Compile(TestSources.Usings + "namespace App;\n" + Entities + """
            public abstract class BaseDb : DbContext
            {
                public DbSet<Blog> Blogs => Set<Blog>();
            }

            public sealed class AppDb : BaseDb
            {
                public DbSet<Post> Posts => Set<Post>();
            }
            """);

        Assert.Equal(["global::App.Blog", "global::App.Post"], EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_EntityTypeConfiguration_IsEntity()
    {
        var compilation = GeneratorHost.Compile(TestSources.Usings + "namespace App;\n" + Entities + """
            public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
            {
                public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Tag> builder) { }
            }
            """);

        Assert.Equal(["global::App.Tag"], EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_NestedContext_IsFound()
    {
        var compilation = GeneratorHost.Compile(TestSources.Usings + "namespace App;\n" + Entities + """
            public static class Outer
            {
                public sealed class AppDb : DbContext
                {
                    public DbSet<Blog> Blogs => Set<Blog>();
                }
            }
            """);

        Assert.Equal(["global::App.Blog"], EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_DbSetOutsideDbContext_IsNotEntity()
    {
        var compilation = GeneratorHost.Compile(TestSources.Usings + "namespace App;\n" + Entities + """
            public sealed class Holder
            {
                public DbSet<Blog>? Blogs { get; set; }
            }
            """);

        Assert.Empty(EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_ContextInReferencedAssembly_IsFound()
    {
        var model = GeneratorHost.CompileReference("Model", TestSources.Usings + "namespace Shop.Model;\n" + Entities + """
            public sealed class ShopDb : DbContext
            {
                public DbSet<Tag> Tags => Set<Tag>();
            }
            """);

        var compilation = GeneratorHost.Compile("public static class Empty { }", model);

        Assert.Equal(["global::Shop.Model.Tag"], EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FromSymbols_WithoutEntityFrameworkCore_IsEmpty()
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("NoEf");

        Assert.Empty(EntityDiscovery.FromSymbols(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Merge_SortsRemovesDuplicatesAndSkipsNulls()
    {
        var fromSymbols = new[] { "global::B", "global::D" }.ToEquatableArray();

        var merged = EntityDiscovery.Merge(fromSymbols, ImmutableArray.Create<string?>("global::A", null, "global::B", "global::C"));

        Assert.Equal(["global::A", "global::B", "global::C", "global::D"], merged);
    }

    [Fact]
    public void ModelBuilderEntityCall_MakesTypeAnEntity()
    {
        var source = TestSources.Usings + "namespace App;\n" + Entities + """
            public sealed class AppDb : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Tag>();

                public void Query() => Set<Tag>().Select(t => t.Id).ToList();
            }
            """;

        var run = GeneratorHost.Run(source);

        Assert.Equal("KP1001", Assert.Single(run.Diagnostics).Id);
    }

    [Fact]
    public void SetCall_MakesTypeAnEntity()
    {
        var source = TestSources.Usings + "namespace App;\n" + Entities + """
            public static class Queries
            {
                public static void Run(DbContext db) => db.Set<Tag>().Select(t => t.Id).ToList();
            }
            """;

        var run = GeneratorHost.Run(source);

        Assert.Equal("KP1001", Assert.Single(run.Diagnostics).Id);
    }

    [Fact]
    public void QueryOnEntityFromReferencedModel_IsAnalyzed()
    {
        var model = GeneratorHost.CompileReference("SharedModel", TestSources.Usings + "namespace Shop.Model;\n" + Entities + """
            public sealed class ShopDb : DbContext
            {
                public DbSet<Tag> Tags => Set<Tag>();
            }
            """);
        var source = TestSources.Usings + """
            using Shop.Model;

            public static class Queries
            {
                public static void Run(ShopDb db) => db.Tags.Select(t => t.Id).ToList();
            }
            """;

        var run = GeneratorHost.Run(source, interceptorsEnabled: true, model);

        Assert.Equal("KP1001", Assert.Single(run.Diagnostics).Id);
    }
}
