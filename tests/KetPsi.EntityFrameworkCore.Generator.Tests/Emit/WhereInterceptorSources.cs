using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class WhereInterceptorSources
{
    /// <summary>Queries that capture values in every way the C# compiler builds them into expression trees.</summary>
    public static readonly string Source = TestSources.Usings + """
        namespace E2E;

        public enum Status { Draft, Published }

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Rating { get; set; }
            public bool IsActive { get; set; }
            public int? Views { get; set; }
            public Status Status { get; set; }
        }

        public sealed class E2EDb : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer("Server=unused;Database=unused");

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Blog>().ToTable("Blogs");
        }

        public sealed record Filter(string Name);

        public sealed class Repository(E2EDb db)
        {
            public static int Ceiling = 50;

            private readonly int _minimum = 4;

            public IQueryable<Blog> ByParameter(int min) => db.Blogs.Where(b => b.Rating > min);

            public IQueryable<Blog> ByLocalAndField()
            {
                var name = "x";
                return db.Blogs.Where(b => b.Name == name && b.Rating >= _minimum);
            }

            public IQueryable<Blog> ByStatic() => db.Blogs.Where(b => b.Rating < Ceiling);

            public IQueryable<Blog> ByChain(Filter filter) => db.Blogs.Where(b => b.Name == filter.Name);

            public IQueryable<Blog> ByEnum() => db.Blogs.Where(b => b.Status == Status.Published);

            public IQueryable<Blog> ByConstant() => db.Blogs.Where(b => b.Rating >= 3);

            public IQueryable<Blog> ByNullable(int? views) => db.Blogs.Where(b => b.Views == views);

            public IQueryable<Blog> ByList(List<int> ids) => db.Blogs.Where(b => ids.Contains(b.Id));

            public IQueryable<Blog> ByArray(int[] ids) => db.Blogs.Where(b => ids.Contains(b.Id));

            public IQueryable<Blog> ByNot() => db.Blogs.Where(b => !(b.IsActive || b.Views.HasValue));

            public IQueryable<Blog> Optional(Filter? filter) => db.Blogs.WhereIf(filter != null, b => b.Name == filter!.Name);

            public IQueryable<Blog> Composed(int? min)
            {
                IQueryable<Blog> query = db.Blogs;
                if (min is not null)
                {
                    query = query.Where(b => b.Rating > min);
                }

                return query.Where(b => b.IsActive);
            }

            public static List<Blog> InMemory(List<Blog> blogs, int min) => blogs.AsQueryable().Where(b => b.Rating > min).ToList();
        }
        """;
}
