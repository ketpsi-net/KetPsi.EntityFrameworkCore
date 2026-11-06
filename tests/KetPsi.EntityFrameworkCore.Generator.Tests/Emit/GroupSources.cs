using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class GroupSources
{
    /// <summary>GroupBy with every supported key, aggregate and operator on its groups, over a context whose connection the test supplies.</summary>
    public static readonly string Source = TestSources.Usings + """
        using System.Data.Common;

        namespace Shop;

        public enum Status { Draft, Published }

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? Views { get; set; }
            public decimal Price { get; set; }
            public Status Status { get; set; }
        }

        public sealed class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
            public Blog Blog { get; set; } = null!;
        }

        public sealed class ShopDb(DbConnection connection) : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();

            public DbSet<Post> Posts => Set<Post>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Blog>().ToTable("Blogs");
                modelBuilder.Entity<Post>().ToTable("Posts");
            }
        }

        public sealed class Queries(ShopDb db)
        {
            public object KeyAndCount() => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

            public object Composite() => db.Blogs.GroupBy(b => new { b.Status, b.Name }).Select(g => new { g.Key, g.Key.Name, Count = g.LongCount() }).ToList();

            public List<IGrouping<int?, int>> IdsByViews() => db.Blogs.GroupBy(b => b.Views, b => b.Id).ToList();

            public List<int?> ViewKeys() => db.Blogs.GroupBy(b => b.Views).Select(g => g.Key).ToList();

            public object Aggregates() => db.Blogs.GroupBy(b => b.Status).Select(g => new
            {
                Sum = g.Sum(b => b.Id),
                Average = g.Average(b => b.Id),
                Min = g.Min(b => b.Name),
                Max = g.Max(b => b.Views),
                Price = g.Sum(b => b.Price),
            }).ToList();

            public object Counts(int min) => db.Blogs.GroupBy(b => b.Status).Select(g => new
            {
                Above = g.Count(b => b.Id > min),
                Named = g.Where(b => b.Name != "").LongCount(),
                Names = g.Select(b => b.Name).Distinct().Count(),
            }).ToList();

            public List<int?> Having(int min) => db.Blogs.GroupBy(b => b.Views).Where(g => g.Count() > min && g.Key != null).Select(g => g.Key).ToList();

            public object WhereAndOrderAfterSelect(int min) => db.Blogs.GroupBy(b => b.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .Where(x => x.Count >= min)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Key)
                .ToList();

            public object OrderedOnGroups() => db.Blogs.GroupBy(b => b.Views).OrderByDescending(g => g.Sum(b => b.Price)).Select(g => g.Key).Take(2).ToList();

            public object Computed(string suffix) => db.Blogs.GroupBy(b => b.Status).Select(g => new { Label = g.Key + suffix, Double = g.Count() * 2 }).ToList();

            public object ResultSelector() => db.Blogs.GroupBy(b => b.Status, b => b.Id, (key, ids) => new { key, Total = ids.Sum() }).ToList();

            public object ThroughNavigation() => db.Posts.GroupBy(p => p.Blog.Name).Select(g => new { g.Key, Count = g.Count() }).ToList();

            public int CountGroups() => db.Blogs.GroupBy(b => b.Status).Count();

            public Task<bool> AnyGroupAsync(int min, CancellationToken ct) => db.Blogs.GroupBy(b => b.Status).AnyAsync(g => g.Count() > min, ct);

            public object FirstGroup() => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).OrderBy(x => x.Key).First();

            public int MaxCount() => db.Blogs.GroupBy(b => b.Status).Select(g => g.Count()).Max();

            public List<IGrouping<Status, Blog>> Groups() => db.Blogs.GroupBy(b => b.Status).ToList();

            public Task<IGrouping<Status, string>[]> NamesByStatusAsync(CancellationToken ct) => db.Blogs.GroupBy(b => b.Status, b => b.Name).ToArrayAsync(ct);

            public object CompositeGroups() => db.Blogs.GroupBy(b => new { b.Status, b.Views }).ToList();
        }
        """;
}
