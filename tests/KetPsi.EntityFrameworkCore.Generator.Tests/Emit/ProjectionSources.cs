using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class ProjectionSources
{
    /// <summary>Every supported projection shape and the operators after Select, over a context whose connection the test supplies.</summary>
    public static readonly string Source = TestSources.Usings + """
        using System.Data.Common;

        namespace Shop;

        public enum Status { Draft, Published }

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? Views { get; set; }
            public Status Status { get; set; }
            public int? OwnerId { get; set; }
            public Author? Owner { get; set; }
            public List<Post> Posts { get; set; } = [];
        }

        public sealed class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
            public string Title { get; set; } = "";
            public Blog Blog { get; set; } = null!;
        }

        public sealed class Author
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed record BlogRow(int Id, string Name);

        public static class Limits
        {
            public static int Floor = 3;
        }

        public static class TextExtensions
        {
            public static string Shout(this string text) => text + "!!";
        }

        public sealed class BlogDto
        {
            public int Id { get; set; }
            public string Name { get; init; } = "";
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
                modelBuilder.Entity<Author>().ToTable("Authors");
            }
        }

        public sealed class Queries(ShopDb db)
        {
            private readonly string _prefix = ">";

            public object Computed(string suffix) => db.Blogs.Select(b => new
            {
                b.Id,
                Label = b.Name + suffix,
                Upper = b.Name.ToUpper(),
                b.Name.Length,
                Bounded = Math.Max(b.Id, Limits.Floor),
                Views = b.Views ?? 0,
                State = b.Status == Status.Draft ? "draft" : "published",
                Text = $"{b.Id}:{_prefix}",
                Shout = b.Name.Shout(),
            }).ToList();

            public List<string> UpperNames() => db.Blogs.Select(b => b.Name.ToUpper()).ToList();

            public List<string> DistinctNames() => db.Blogs.Select(b => b.Name).Distinct().ToList();

            public List<Blog> DistinctBlogs() => db.Blogs.Distinct().ToList();

            public int? SumViews() => db.Blogs.Sum(b => b.Views);

            public Task<double> AverageIdAsync(CancellationToken ct) => db.Blogs.AverageAsync(b => b.Id, ct);

            public string? MaxOwnerName() => db.Blogs.Max(b => b.Owner!.Name);

            public int SumIds() => db.Blogs.Select(b => b.Id).Sum();

            public string? MinName() => db.Blogs.Select(b => b.Name).Min();

            public Task<int> MaxIdAsync(CancellationToken ct) => db.Blogs.Select(b => b.Id).MaxAsync(ct);

            public object Anonymous() => db.Blogs.Select(b => new { b.Id, b.Name }).ToList();

            public object Nested() => db.Blogs.Select(b => new { b.Id, Inner = new { b.Name, b.Status, b.Views } }).ToList();

            public List<BlogRow> Records() => db.Blogs.Select(b => new BlogRow(b.Id, b.Name)).ToList();

            public List<BlogDto> Initializers() => db.Blogs.Select(b => new BlogDto { Id = b.Id, Name = b.Name }).ToList();

            public List<string> Names() => db.Blogs.Select(b => b.Name).ToList();

            public List<(int, int?)> Tuples() => db.Blogs.Select(b => ValueTuple.Create(b.Id, b.Views)).ToList();

            public List<object> Boxed() => db.Blogs.Select<Blog, object>(b => b.Id).ToList();

            public List<long> Widened() => db.Blogs.Select(b => (long)b.Id).ToList();

            public List<string> OwnerNames() => db.Blogs.Select(b => b.Owner!.Name).ToList();

            public List<int?> NullableOwnerIds() => db.Blogs.Select(b => (int?)b.Owner!.Id).ToList();

            public List<int> OwnerIds() => db.Blogs.Select(b => b.Owner!.Id).ToList();

            public List<Author?> Owners() => db.Blogs.Select(b => b.Owner).ToList();

            public object PostsWithBlogs() => db.Posts.Select(p => new { p, p.Blog, BlogName = p.Blog.Name }).ToList();

            public object BlogsWithPosts() => db.Blogs.Include(b => b.Posts).Select(b => new { b, b.Name }).ToList();

            public List<string> Page(int skip, int take) => db.Blogs.OrderBy(b => b.Name).Select(b => b.Name).Skip(skip).Take(take).ToList();

            public string[] Array() => db.Blogs.Select(b => b.Name).ToArray();

            public Task<List<string>> ListAsync(CancellationToken ct) => db.Blogs.Select(b => b.Name).ToListAsync(ct);

            public Task<string[]> ArrayAsync(CancellationToken ct) => db.Blogs.Select(b => b.Name).ToArrayAsync(ct);

            public string First() => db.Blogs.Select(b => b.Name).First();

            public Task<string?> FirstOrDefaultAsync(CancellationToken ct) => db.Blogs.Select(b => b.Name).FirstOrDefaultAsync(ct);

            public BlogRow Single() => db.Blogs.Select(b => new BlogRow(b.Id, b.Name)).Single();

            public Task<BlogRow?> SingleOrDefaultAsync(CancellationToken ct) => db.Blogs.Select(b => new BlogRow(b.Id, b.Name)).SingleOrDefaultAsync(ct);

            public int Count() => db.Blogs.Select(b => b.Owner!.Name).Count();

            public Task<long> LongCountAsync(CancellationToken ct) => db.Blogs.Select(b => b.Name).LongCountAsync(ct);

            public bool Any() => db.Blogs.Select(b => b.Id).Any();

            public Task<bool> AnyAsync(CancellationToken ct) => db.Blogs.Select(b => b.Id).AnyAsync(ct);

            public List<string> FromVariable()
            {
                var names = db.Blogs.Select(b => b.Name);
                return names.Take(2).ToList();
            }

            public static List<string> InMemory(List<Blog> blogs) => blogs.AsQueryable().Select(b => b.Name).Take(1).ToList();
        }
        """;
}
