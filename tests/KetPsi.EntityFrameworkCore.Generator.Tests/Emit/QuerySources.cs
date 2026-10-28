using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class QuerySources
{
    /// <summary>Every supported operator and terminal, over a context whose connection the test supplies.</summary>
    public static readonly string Source = TestSources.Usings + """
        using System.Data.Common;

        namespace Shop;

        public enum Status { Draft, Published }

        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Rating { get; set; }
            public bool IsActive { get; set; }
            public int? Views { get; set; }
            public Status Status { get; set; }
            public List<Post> Posts { get; set; } = [];
        }

        public sealed class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
            public string Title { get; set; } = "";
            public Blog Blog { get; set; } = null!;
        }

        public sealed class ShopDb(DbConnection connection) : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();

            public DbSet<Post> Posts => Set<Post>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer(connection);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Blog>().ToTable("Blogs");
                modelBuilder.Entity<Post>().ToTable("Posts");
            }
        }

        public sealed class Queries(ShopDb db)
        {
            public List<Blog> Page(int min, int skip, int take)
                => db.Blogs.AsNoTracking().Where(b => b.Rating >= min).OrderBy(b => b.Name).ThenByDescending(b => b.Id).Skip(skip).Take(take).ToList();

            public Blog[] ByRating() => db.Blogs.AsNoTracking().OrderByDescending(b => b.Rating).ThenBy(b => b.Id).ToArray();

            public Task<List<Blog>> ListAsync(CancellationToken ct) => db.Blogs.AsNoTracking().ToListAsync(ct);

            public Task<Blog[]> ArrayAsync(CancellationToken ct) => db.Blogs.AsNoTracking().ToArrayAsync(ct);

            public Blog First(int id) => db.Blogs.AsNoTracking().First(b => b.Id == id);

            public Task<Blog> FirstAsync(CancellationToken ct) => db.Blogs.AsNoTracking().FirstAsync(ct);

            public Blog? FirstOrDefault() => db.Blogs.AsNoTracking().FirstOrDefault();

            public Task<Blog?> FirstOrDefaultAsync(string name, CancellationToken ct)
                => db.Blogs.AsNoTracking().FirstOrDefaultAsync(b => b.Name == name, ct);

            public Blog Single() => db.Blogs.AsNoTracking().Single();

            public Task<Blog> SingleAsync(int id, CancellationToken ct) => db.Blogs.AsNoTracking().SingleAsync(b => b.Id == id, ct);

            public Blog? SingleOrDefault(int id) => db.Blogs.AsNoTracking().SingleOrDefault(b => b.Id == id);

            public Task<Blog?> SingleOrDefaultAsync(CancellationToken ct) => db.Blogs.AsNoTracking().SingleOrDefaultAsync(ct);

            public int Count() => db.Blogs.Count();

            public Task<int> CountAsync(bool active, CancellationToken ct) => db.Blogs.CountAsync(b => b.IsActive == active, ct);

            public long LongCount() => db.Blogs.Where(b => b.IsActive).LongCount();

            public Task<long> LongCountAsync(CancellationToken ct) => db.Blogs.LongCountAsync(ct);

            public bool Any(int min) => db.Blogs.Any(b => b.Rating > min);

            public Task<bool> AnyAsync(CancellationToken ct) => db.Blogs.AnyAsync(ct);

            public List<Blog> IgnoringFilters() => db.Blogs.IgnoreQueryFilters().AsNoTracking().ToList();

            public int IgnoringNamedFilter() => db.Blogs.IgnoreQueryFilters(["SoftDelete"]).Count();

            public List<Blog> Composed(bool byName)
            {
                IQueryable<Blog> query = db.Blogs.AsNoTracking();
                query = byName ? query.OrderBy(b => b.Name) : query.OrderByDescending(b => b.Rating);
                return query.Take(5).ToList();
            }

            public List<Blog> Tracked(int min) => db.Blogs.AsTracking().Where(b => b.Rating >= min).ToList();

            public List<Blog> TrackedWithBehavior(QueryTrackingBehavior behavior) => db.Blogs.AsNoTracking().AsTracking(behavior).ToList();

            public List<Blog> IdentityResolved() => db.Blogs.AsNoTrackingWithIdentityResolution().ToList();

            public List<Blog> WithPosts() => db.Blogs.AsNoTracking().Include(b => b.Posts).ToList();

            public List<Post> PostsWithBlogAndSiblings() => db.Posts.Include(p => p.Blog).ThenInclude(b => b.Posts).AsSplitQuery().ToList();

            public List<Blog> TrackedAcrossStatements()
            {
                IQueryable<Blog> query = db.Blogs;
                return query.ToList();
            }

            public static List<Blog> InMemory(List<Blog> blogs) => blogs.AsQueryable().OrderBy(b => b.Rating).Take(1).ToList();
        }
        """;
}
