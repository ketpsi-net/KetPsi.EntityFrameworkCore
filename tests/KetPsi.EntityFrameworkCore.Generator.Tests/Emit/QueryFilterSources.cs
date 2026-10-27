using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class QueryFilterSources
{
    /// <summary>Filters whose values come from constants, statics and the context, public and private.</summary>
    public static readonly string Source = TestSources.Usings + """
        using System.Data.Common;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace Filtered;

        public enum Status { Draft, Published }

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Rating { get; set; }
            public bool IsActive { get; set; }
            public bool IsDeleted { get; set; }
            public int TenantId { get; set; }
            public Status Status { get; set; }
            public int? Views { get; set; }
        }

        public sealed class Tag
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public static class FilterDefaults
        {
            public static int MinViews = 100;
        }

        public sealed class FilteredDb(DbConnection connection, int tenantId) : DbContext
        {
            private readonly int _tenantId = tenantId;

            public int MinimumRating { get; set; } = 2;

            public DbSet<Blog> Blogs => Set<Blog>();

            public DbSet<Tag> Tags => Set<Tag>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer(connection);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Blog>(b =>
                {
                    b.ToTable("Blogs");
                    b.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
                    b.HasQueryFilter("Tenant", x => x.TenantId == _tenantId && x.Rating >= MinimumRating);
                    b.HasQueryFilter("Published", x => x.Status != Status.Draft || x.Views > FilterDefaults.MinViews);
                });
                modelBuilder.ApplyConfiguration(new TagConfiguration());
            }
        }

        public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
        {
            public void Configure(EntityTypeBuilder<Tag> builder) => builder.ToTable("Tags").HasQueryFilter(t => t.Name != "hidden");
        }

        public sealed class Queries(FilteredDb db)
        {
            public List<Blog> Blogs() => db.Blogs.AsNoTracking().ToList();

            public List<Blog> WithoutTenant() => db.Blogs.AsNoTracking().IgnoreQueryFilters(["Tenant"]).ToList();

            public List<Blog> Unfiltered() => db.Blogs.AsNoTracking().IgnoreQueryFilters().ToList();

            public int Tags() => db.Tags.Count();
        }
        """;
}
