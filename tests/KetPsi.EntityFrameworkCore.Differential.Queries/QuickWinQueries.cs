// Last, navigations in predicates and orderings, Find, ExecuteDelete and Join, compiled into both query builds like the
// rest of the catalog.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    // Last and LastOrDefault read the ordering in reverse.
    public static Blog LastByName(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Name).Last();

    public static Blog? LastOrDefaultBelow(DiffDb db, int rating)
        => db.Blogs.AsNoTracking().OrderBy(b => b.Rating).ThenBy(b => b.Id).LastOrDefault(b => b.Rating < rating);

    public static Blog LastOfPage(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Take(3).Last();

    public static Task<Blog> LastAsyncAfterSkip(DiffDb db) => db.Blogs.AsNoTracking().OrderByDescending(b => b.Price).Skip(2).LastAsync();

    public static string LastProjected(DiffDb db) => db.Blogs.OrderBy(b => b.Rating).ThenByDescending(b => b.Id).Select(b => b.Name).Last();

    public static Task<Blog?> LastOrDefaultAsyncNone(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Rating > 100).OrderBy(b => b.Id).LastOrDefaultAsync();

    public static Blog LastWithoutOrdering(DiffDb db) => db.Blogs.AsNoTracking().Last();

    public static Blog LastAfterDistinct(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Name).Distinct().Last();

    // Reference navigations in predicates and orderings. Blog is filtered, so a post of a deleted blog has no blog.
    public static List<Post> PostsOfBlogNamed(DiffDb db, string name) => db.Posts.AsNoTracking().Where(p => p.Blog.Name == name).ToList();

    public static List<Post> PostsOrderedByBlog(DiffDb db, int rating)
        => db.Posts.AsNoTracking().Where(p => p.Blog.Rating >= rating).OrderByDescending(p => p.Blog.Name).ThenBy(p => p.Id).ToList();

    public static List<Blog> BlogsByAuthor(DiffDb db, string? name) => db.Blogs.AsNoTracking().Where(b => b.Author!.Name == name).ToList();

    public static List<Blog> BlogsNotByAuthor(DiffDb db, string? name) => db.Blogs.AsNoTracking().Where(b => b.Author!.Name != name).ToList();

    public static List<Blog> BlogsWithoutAuthor(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Author == null).ToList();

    public static List<Blog> BlogsWithAuthor(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Author != null && b.Author.Id != 2).ToList();

    public static List<Post> PostsWithBlog(DiffDb db) => db.Posts.AsNoTracking().IgnoreQueryFilters().Where(p => p.Blog != null).ToList();

    public static List<Post> PostsOfBlogId(DiffDb db, int id) => db.Posts.AsNoTracking().Where(p => p.Blog.Id == id).ToList();

    public static int CountPostsOfActiveBlogs(DiffDb db) => db.Posts.Count(p => p.Blog.IsActive);

    public static Task<bool> AnyCommentOnRatedBlog(DiffDb db, int rating) => db.Comments.AnyAsync(c => c.Post.Blog.Rating >= rating);

    public static List<Blog> BlogsOrderedByAuthor(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Author!.Name).ThenBy(b => b.Id).ToList();

    public static List<Post> PostsOfBlogNamedWithInclude(DiffDb db, string name)
        => db.Posts.AsNoTracking().Include(p => p.Blog).Where(p => p.Blog.Name != name).ToList();

    // Find looks in the change tracker first, then queries by key with the filters.
    public static Blog? FindBlog(DiffDb db, int id) => db.Blogs.Find(id);

    public static async Task<Post?> FindPostAsync(DiffDb db, int id) => await db.Posts.FindAsync(id);

    public static bool FindTrackedTwice(DiffDb db, int id) => ReferenceEquals(db.Blogs.Find(id), db.Blogs.Find(id));

    // Find(null) queries nothing; the count after it is the only query, on both sides.
    public static object FindNull(DiffDb db) => new { Found = db.Blogs.Find((object?)null), Blogs = db.Blogs.Count() };

    public static Blog? FindWrongType(DiffDb db) => db.Blogs.Find(1L);

    public static Blog? FindTwoValues(DiffDb db) => db.Blogs.Find(1, 2);

    // ExecuteDelete in a transaction that is rolled back, so the rows stay for the other cases.
    public static object DeleteCommentsOfPost(DiffDb db, int postId)
        => InRolledBackTransaction(db, () => db.Comments.Where(c => c.PostId == postId).ExecuteDelete());

    public static object DeleteCommentsThroughNavigation(DiffDb db, string name)
        => InRolledBackTransaction(db, () => db.Comments.Where(c => c.Post.Blog.Name == name).ExecuteDelete());

    public static object DeleteCommentsPaged(DiffDb db)
        => InRolledBackTransaction(db, () => db.Comments.OrderBy(c => c.Text).Skip(1).Take(2).ExecuteDeleteAsync().GetAwaiter().GetResult());

    public static object DeleteFilteredPosts(DiffDb db)
        => InRolledBackTransaction(db, () => db.Posts.Where(p => p.Id == 104 || p.Id == 502 || p.Id == 1001).ExecuteDelete());

    public static object DeleteBlogsWithPosts(DiffDb db) => InRolledBackTransaction(db, () => db.Blogs.Where(b => b.Rating == 5).ExecuteDelete());

    public static object DeleteTrackedComment(DiffDb db)
        => InRolledBackTransaction(db, () =>
        {
            var comment = db.Comments.Single(c => c.Id == 3);
            var deleted = db.Comments.Where(c => c.Id == 3).ExecuteDelete();
            return deleted * 10 + (int)db.Entry(comment).State;
        });

    // Join, and the operators that read through its result.
    public static object JoinColumns(DiffDb db) => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Title, b.Name }).ToList();

    public static object JoinEntities(DiffDb db) => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b }).ToList();

    public static object JoinInnerFiltered(DiffDb db, int rating)
        => db.Posts.AsNoTracking().Join(db.Blogs.AsNoTracking().Where(b => b.Rating >= rating), p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Rating }).ToList();

    public static object JoinThroughThePair(DiffDb db, int rating)
        => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
            .Where(x => x.b.Rating >= rating && x.p.Title != "p101")
            .OrderBy(x => x.b.Name)
            .ThenByDescending(x => x.p.Id)
            .Select(x => new { x.p.Title, Blog = x.b.Name, x.b.Views })
            .ToList();

    public static object JoinTwice(DiffDb db)
        => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
            .Join(db.Comments, x => x.p.Id, c => c.PostId, (x, c) => new { x.b.Name, x.p.Title, c.Text })
            .ToList();

    public static object JoinCompositeKey(DiffDb db)
        => db.Blogs.Join(db.Authors, b => new { Id = b.AuthorId, b.Name }, a => new { Id = (int?)a.Id, a.Name }, (b, a) => b.Id).ToList();

    public static object JoinNullableKey(DiffDb db) => db.Blogs.Join(db.Authors, b => b.AuthorId, a => (int?)a.Id, (b, a) => new { b.Id, a.Name }).ToList();

    public static object JoinOuterKeyThroughNavigation(DiffDb db)
        => db.Posts.Join(db.Authors, p => p.Blog.AuthorId, a => (int?)a.Id, (p, a) => new { p.Id, Author = a.Name }).ToList();

    public static object JoinAfterPaging(DiffDb db)
        => db.Posts.OrderBy(p => p.Id).Take(4).Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).ToList();

    public static object JoinPagedAfter(DiffDb db, string suffix)
        => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p.Id, Label = b.Name + suffix }).OrderBy(x => x.Id).Skip(1).Take(3).ToList();

    public static int JoinCount(DiffDb db) => db.Posts.Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => p.Id).Count();

    public static Task<string> JoinFirstAsync(DiffDb db) => db.Posts.OrderByDescending(p => p.Id).Join(db.Authors, p => p.Blog.AuthorId, a => (int?)a.Id, (p, a) => a.Name).FirstAsync();

    public static object JoinIgnoringInnerFilters(DiffDb db)
        => db.Posts.AsNoTracking().Join(db.Blogs.IgnoreQueryFilters(), p => p.BlogId, b => b.Id, (p, b) => new { p.Id, b.Name }).ToList();

    public static object SelectEntitiesThenWhere(DiffDb db, int rating)
        => db.Posts.Select(p => new { p, p.Blog }).Where(x => x.Blog.Rating > rating).OrderBy(x => x.p.Title).Select(x => x.p.Title).ToList();

    public static List<int> SelectColumnThenWhere(DiffDb db, int rating) => db.Blogs.Select(b => b.Rating).Where(r => r > rating).ToList();

    private static object InRolledBackTransaction(DiffDb db, Func<int> delete)
    {
        using var transaction = db.Database.BeginTransaction();
        try
        {
            var deleted = delete();
            return new { Deleted = deleted, Comments = db.Comments.IgnoreQueryFilters().Count(), Posts = db.Posts.IgnoreQueryFilters().Count() };
        }
        finally
        {
            transaction.Rollback();
        }
    }
}
