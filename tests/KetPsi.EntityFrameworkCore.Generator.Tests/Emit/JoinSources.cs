using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class JoinSources
{
    /// <summary>Join in method and query syntax, with the operators that read through its pair, over a context whose connection the test supplies.</summary>
    public static readonly string Source = TestSources.Usings + """
        using System.Data.Common;

        namespace Shop;

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Rating { get; set; }
        }

        public sealed class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
            public string Title { get; set; } = "";
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
            public object Columns() => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Title, b.Name }).ToList();

            public object ThroughThePair(int min) => db.Posts
                .Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
                .Where(x => x.b.Rating > min)
                .OrderBy(x => x.b.Name)
                .ThenByDescending(x => x.p.Title)
                .Select(x => new { x.p.Title, x.b.Name })
                .ToList();

            public object Entities(string name) => db.Posts.Join(db.Blogs.Where(b => b.Name != name), p => p.BlogId, b => b.Id, (p, b) => new { p, b }).ToList();

            public object TwoJoins() => db.Posts
                .Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
                .Join(db.Posts, x => x.b.Id, o => o.BlogId, (x, o) => new { x.p.Title, Other = o.Title })
                .ToList();

            public int CountThroughThePair(int min) => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).Count(x => x.b.Rating > min);

            public Task<string?> FirstColumnAsync(string name, CancellationToken ct)
                => db.Blogs.Select(b => b.Name).OrderBy(n => n).FirstOrDefaultAsync(n => n != name, ct);

            public int CountJoined() => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => p.Id).Count();

            public Task<string> FirstNameAsync(string suffix, CancellationToken ct)
                => db.Posts.OrderBy(p => p.Id).Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => b.Name + suffix).FirstAsync(ct);
        }
        """;
}
