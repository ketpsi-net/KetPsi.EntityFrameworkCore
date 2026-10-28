// Compiled twice: into Differential.Queries.Stock, which runs on EF Core, and into Differential.Queries.Intercepted,
// where KetPsi.EntityFrameworkCore.Generator intercepts every query. Both must return the same results.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public sealed record RatingFilter(int Minimum);

public static class Queries
{
    public static int Threshold = 4;

    private static readonly int[] FeaturedIds = [1, 3, 5];

    // Comparisons on non-nullable columns.
    public static List<Blog> RatingEquals(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating == rating).ToList();

    public static List<Blog> RatingNotEquals(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating != rating).ToList();

    public static List<Blog> RatingBelow(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating < rating).ToList();

    public static List<Blog> RatingAtMost(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating <= rating).ToList();

    public static List<Blog> RatingAbove(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating > rating).ToList();

    public static List<Blog> RatingAtLeast(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => b.Rating >= rating).ToList();

    public static List<Blog> RatingAboveConstant(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Rating > 3).ToList();

    public static List<Blog> ConstantOnLeft(DiffDb db) => db.Blogs.AsNoTracking().Where(b => 3 < b.Rating).ToList();

    public static List<Blog> NotRatingBelow(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => !(b.Rating < rating)).ToList();

    public static List<Blog> NameEquals(DiffDb db, string? name) => db.Blogs.AsNoTracking().Where(b => b.Name == name).ToList();

    public static List<Blog> NameNotEquals(DiffDb db, string? name) => db.Blogs.AsNoTracking().Where(b => b.Name != name).ToList();

    public static List<Blog> NameEqualsMethod(DiffDb db, string name) => db.Blogs.AsNoTracking().Where(b => b.Name.Equals(name)).ToList();

    public static List<Blog> CreatedAfter(DiffDb db, DateTime since) => db.Blogs.AsNoTracking().Where(b => b.CreatedAt > since).ToList();

    public static List<Blog> PriceAtLeast(DiffDb db, decimal price) => db.Blogs.AsNoTracking().Where(b => b.Price >= price).ToList();

    public static List<Blog> ByExternalId(DiffDb db, Guid id) => db.Blogs.AsNoTracking().Where(b => b.ExternalId == id).ToList();

    // Nullable column with a captured value, which may be null.
    public static List<Blog> ViewsEquals(DiffDb db, int? views) => db.Blogs.AsNoTracking().Where(b => b.Views == views).ToList();

    public static List<Blog> ViewsNotEquals(DiffDb db, int? views) => db.Blogs.AsNoTracking().Where(b => b.Views != views).ToList();

    public static List<Blog> ViewsAbove(DiffDb db, int? views) => db.Blogs.AsNoTracking().Where(b => b.Views > views).ToList();

    public static List<Blog> NotViewsAbove(DiffDb db, int? views) => db.Blogs.AsNoTracking().Where(b => !(b.Views > views)).ToList();

    public static List<Blog> NotViewsEquals(DiffDb db, int? views) => db.Blogs.AsNoTracking().Where(b => !(b.Views == views)).ToList();

    // Booleans and null checks.
    public static List<Blog> Active(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.IsActive).ToList();

    public static List<Blog> Inactive(DiffDb db) => db.Blogs.AsNoTracking().Where(b => !b.IsActive).ToList();

    public static List<Blog> ActiveEquals(DiffDb db, bool active) => db.Blogs.AsNoTracking().Where(b => b.IsActive == active).ToList();

    public static List<Blog> ViewsNull(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Views == null).ToList();

    public static List<Blog> ViewsNotNull(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Views != null).ToList();

    public static List<Blog> ViewsHasValue(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Views.HasValue).ToList();

    public static List<Blog> ViewsHasNoValue(DiffDb db) => db.Blogs.AsNoTracking().Where(b => !b.Views.HasValue).ToList();

    // Logical operators.
    public static List<Blog> AndOr(DiffDb db, int rating)
        => db.Blogs.AsNoTracking().Where(b => (b.IsActive && b.Rating >= rating) || b.Views == null).ToList();

    public static List<Blog> NotAnd(DiffDb db, int views) => db.Blogs.AsNoTracking().Where(b => !(b.IsActive && b.Views > views)).ToList();

    public static List<Blog> NotOr(DiffDb db, int rating) => db.Blogs.AsNoTracking().Where(b => !(b.Rating > rating || b.Views == null)).ToList();

    // Enums.
    public static List<Blog> StatusIs(DiffDb db, Status status) => db.Blogs.AsNoTracking().Where(b => b.Status == status).ToList();

    public static List<Blog> NotDraft(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Status != Status.Draft).ToList();

    // Contains.
    public static List<Blog> IdsIn(DiffDb db, List<int> ids) => db.Blogs.AsNoTracking().Where(b => ids.Contains(b.Id)).ToList();

    public static List<Blog> IdsNotIn(DiffDb db, int[] ids) => db.Blogs.AsNoTracking().Where(b => !ids.Contains(b.Id)).ToList();

    public static List<Blog> ViewsIn(DiffDb db, List<int?> views) => db.Blogs.AsNoTracking().Where(b => views.Contains(b.Views)).ToList();

    public static List<Blog> ViewsNotIn(DiffDb db, List<int?> views) => db.Blogs.AsNoTracking().Where(b => !views.Contains(b.Views)).ToList();

    public static List<Blog> NamesIn(DiffDb db, string[] names) => db.Blogs.AsNoTracking().Where(b => names.Contains(b.Name)).ToList();

    public static List<Blog> StatusesIn(DiffDb db, List<Status> statuses)
        => db.Blogs.AsNoTracking().Where(b => statuses.Contains(b.Status)).ToList();

    // Captured values.
    public static List<Blog> StaticField(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Rating > Threshold).ToList();

    public static List<Blog> StaticArray(DiffDb db) => db.Blogs.AsNoTracking().Where(b => FeaturedIds.Contains(b.Id)).ToList();

    public static List<Blog> PropertyChain(DiffDb db, int minimum)
    {
        var filter = new RatingFilter(minimum);
        return db.Blogs.AsNoTracking().Where(b => b.Rating >= filter.Minimum).ToList();
    }

    // WhereIf and composition across statements.
    public static List<Blog> WhereIfName(DiffDb db, string? name) => db.Blogs.AsNoTracking().WhereIf(name != null, b => b.Name == name).ToList();

    public static List<Blog> SeveralWheres(DiffDb db, int rating)
        => db.Blogs.AsNoTracking().Where(b => b.IsActive).Where(b => b.Rating >= rating).ToList();

    public static List<Blog> Composed(DiffDb db, int? rating, bool activeOnly)
    {
        var query = db.Blogs.AsNoTracking();
        if (rating is not null)
        {
            query = query.Where(b => b.Rating >= rating);
        }

        if (activeOnly)
        {
            query = query.Where(b => b.IsActive);
        }

        return query.OrderBy(b => b.Id).ToList();
    }

    // Ordering and paging; the test compares these in order.
    public static List<Blog> OrderByName(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Name).ThenBy(b => b.Id).ToList();

    public static List<Blog> OrderByRatingDescending(DiffDb db)
        => db.Blogs.AsNoTracking().OrderByDescending(b => b.Rating).ThenBy(b => b.Id).ToList();

    public static List<Blog> OrderByNullableViews(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Views).ThenBy(b => b.Id).ToList();

    public static List<Blog> OrderByStatusThenNameDescending(DiffDb db)
        => db.Blogs.AsNoTracking().OrderByDescending(b => b.Status).ThenByDescending(b => b.Name).ThenBy(b => b.Id).ToList();

    public static List<Blog> SecondOrderByReplaces(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Name).OrderBy(b => b.Id).ToList();

    public static List<Blog> Page(DiffDb db, int skip, int take) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Skip(skip).Take(take).ToList();

    public static List<Blog> SkipOnly(DiffDb db, int skip) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Skip(skip).ToList();

    public static List<Blog> TakeOnly(DiffDb db, int take) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Take(take).ToList();

    public static List<Blog> TakeThenWhere(DiffDb db)
        => db.Blogs.AsNoTracking().OrderByDescending(b => b.Rating).ThenBy(b => b.Id).Take(4).Where(b => b.IsActive).ToList();

    public static List<Blog> TakeThenTake(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Take(5).Take(2).ToList();

    public static List<Blog> SkipThenSkip(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Skip(1).Skip(2).ToList();

    public static List<Blog> TakeThenOrderBy(DiffDb db)
        => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Take(4).OrderByDescending(b => b.Rating).ThenBy(b => b.Id).ToList();

    public static List<Blog> TakeThenSkip(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Take(5).Skip(2).ToList();

    // Terminals.
    public static Blog[] AllAsArray(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).ToArray();

    public static Blog First(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).First();

    public static Blog FirstWhere(DiffDb db, int rating) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).First(b => b.Rating >= rating);

    public static Blog FirstNone(DiffDb db) => db.Blogs.AsNoTracking().First(b => b.Rating > 100);

    public static Blog? FirstOrDefaultNone(DiffDb db) => db.Blogs.AsNoTracking().FirstOrDefault(b => b.Rating > 100);

    public static Blog Single(DiffDb db, int id) => db.Blogs.AsNoTracking().Single(b => b.Id == id);

    public static Blog SingleTooMany(DiffDb db) => db.Blogs.AsNoTracking().Single();

    public static Blog SingleNone(DiffDb db) => db.Blogs.AsNoTracking().Single(b => b.Rating > 100);

    public static Blog? SingleOrDefaultNone(DiffDb db) => db.Blogs.AsNoTracking().SingleOrDefault(b => b.Rating > 100);

    public static Blog? SingleOrDefaultTooMany(DiffDb db) => db.Blogs.AsNoTracking().SingleOrDefault(b => b.IsActive);

    public static int Count(DiffDb db) => db.Blogs.Count();

    public static int CountWhere(DiffDb db, int rating) => db.Blogs.Count(b => b.Rating >= rating);

    public static long LongCount(DiffDb db) => db.Blogs.Where(b => b.IsActive).LongCount();

    public static int CountAfterTake(DiffDb db) => db.Blogs.OrderBy(b => b.Id).Take(3).Count();

    public static bool Any(DiffDb db) => db.Blogs.Any();

    public static bool AnyWhere(DiffDb db, int rating) => db.Blogs.Any(b => b.Rating > rating);

    public static bool AnyAfterSkip(DiffDb db, int skip) => db.Blogs.OrderBy(b => b.Id).Skip(skip).Any();

    // Async terminals.
    public static Task<List<Blog>> ToListAsync(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).ToListAsync();

    public static Task<Blog[]> ToArrayAsync(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).ToArrayAsync();

    public static Task<Blog> FirstAsync(DiffDb db, int rating) => db.Blogs.AsNoTracking().OrderBy(b => b.Id).FirstAsync(b => b.Rating >= rating);

    public static Task<Blog?> FirstOrDefaultAsyncNone(DiffDb db) => db.Blogs.AsNoTracking().FirstOrDefaultAsync(b => b.Rating > 100);

    public static Task<Blog> FirstAsyncNone(DiffDb db) => db.Blogs.AsNoTracking().FirstAsync(b => b.Rating > 100);

    public static Task<Blog> SingleAsyncNone(DiffDb db) => db.Blogs.AsNoTracking().SingleAsync(b => b.Rating > 100);

    public static Task<Blog> SingleAsync(DiffDb db, int id) => db.Blogs.AsNoTracking().SingleAsync(b => b.Id == id);

    public static Task<Blog?> SingleOrDefaultAsyncTooMany(DiffDb db) => db.Blogs.AsNoTracking().SingleOrDefaultAsync();

    public static Task<int> CountAsync(DiffDb db, int rating) => db.Blogs.CountAsync(b => b.Rating >= rating);

    public static Task<long> LongCountAsync(DiffDb db) => db.Blogs.LongCountAsync();

    public static Task<bool> AnyAsync(DiffDb db, int rating) => db.Blogs.AnyAsync(b => b.Rating > rating);

    // Query filters.
    public static List<Blog> IgnoringFilters(DiffDb db) => db.Blogs.AsNoTracking().IgnoreQueryFilters().OrderBy(b => b.Id).ToList();

    public static List<Blog> DeletedIgnoringFilters(DiffDb db)
        => db.Blogs.AsNoTracking().IgnoreQueryFilters().Where(b => b.IsDeleted).OrderBy(b => b.Id).ToList();

    public static int CountIgnoringFilters(DiffDb db) => db.Blogs.IgnoreQueryFilters().Count();

    // Tracking: the test also compares the change tracker after each query, and the commands SaveChanges sends.
    // Saves run in a transaction that is rolled back, so the data stays as seeded.
    public static List<Blog> Tracked(DiffDb db, int rating) => db.Blogs.Where(b => b.Rating >= rating).ToList();

    public static List<Blog> AsTrackingAfterAsNoTracking(DiffDb db) => db.Blogs.AsNoTracking().AsTracking().Where(b => b.IsActive).ToList();

    public static List<Blog> AsTrackingWithBehavior(DiffDb db, QueryTrackingBehavior behavior)
        => db.Blogs.AsTracking(behavior).Where(b => b.Rating > 3).ToList();

    public static List<Blog> IdentityResolution(DiffDb db) => db.Blogs.AsNoTrackingWithIdentityResolution().Where(b => b.Views != null).ToList();

    public static List<Blog> ContextDefaultNoTracking(DiffDb db)
    {
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return db.Blogs.Where(b => b.IsActive).ToList();
    }

    public static Blog TrackedFirst(DiffDb db) => db.Blogs.OrderBy(b => b.Id).First(b => b.Rating == 4);

    public static Task<List<Blog>> TrackedToListAsync(DiffDb db) => db.Blogs.OrderBy(b => b.Id).Take(3).ToListAsync();

    public static Task<Blog?> TrackedSingleOrDefaultAsync(DiffDb db, int id) => db.Blogs.SingleOrDefaultAsync(b => b.Id == id);

    // A second query returns the tracked instance as it is, not the database values.
    public static List<Blog> RequeryKeepsTrackedValues(DiffDb db)
    {
        var first = db.Blogs.First(b => b.Id == 3);
        first.Name = "changed";
        first.Rating = 1;
        return db.Blogs.Where(b => b.Rating == 5).ToList();
    }

    public static Blog LoadModifySave(DiffDb db, int id)
    {
        using var transaction = db.Database.BeginTransaction();
        var blog = db.Blogs.Single(b => b.Id == id);
        blog.Name = "renamed";
        blog.Views = null;
        db.SaveChanges();
        return blog;
    }

    public static async Task<Blog> LoadModifyShadowSaveAsync(DiffDb db, int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var blog = await db.Blogs.FirstAsync(b => b.Id == id);
        var revision = db.Entry(blog).Property<int>(DiffDb.Revision);
        revision.CurrentValue++;
        await db.SaveChangesAsync();
        return blog;
    }

    public static List<Blog> LoadDeleteSave(DiffDb db)
    {
        using var transaction = db.Database.BeginTransaction();
        var blogs = db.Blogs.Where(b => b.Status == Status.Archived).ToList();
        db.Blogs.RemoveRange(blogs);
        db.SaveChanges();
        return blogs;
    }

    // Include: the test compares the whole graph, which instances are shared, and the change tracker.
    public static List<Blog> IncludePosts(DiffDb db) => db.Blogs.Include(b => b.Posts).ToList();

    public static List<Blog> IncludePostsNoTracking(DiffDb db) => db.Blogs.AsNoTracking().Include(b => b.Posts).ToList();

    public static List<Blog> IncludePostsIdentityResolution(DiffDb db)
        => db.Blogs.AsNoTrackingWithIdentityResolution().Include(b => b.Posts).ToList();

    public static List<Blog> IncludeAuthor(DiffDb db) => db.Blogs.Include(b => b.Author).ToList();

    public static List<Blog> IncludeAuthorNoTracking(DiffDb db) => db.Blogs.AsNoTracking().Include(b => b.Author).ToList();

    public static List<Blog> IncludeAuthorIdentityResolution(DiffDb db)
        => db.Blogs.AsNoTrackingWithIdentityResolution().Include(b => b.Author).ToList();

    // Post 901 belongs to a soft-deleted blog: the required reference's join drops it, as in EF Core.
    public static List<Post> IncludeRequiredBlog(DiffDb db) => db.Posts.Include(p => p.Blog).ToList();

    public static List<Post> IncludeRequiredBlogNoTracking(DiffDb db) => db.Posts.AsNoTracking().Include(p => p.Blog).ToList();

    public static List<Blog> IncludePostsThenComments(DiffDb db)
        => db.Blogs.Include(b => b.Posts).ThenInclude(p => p.Comments).Include(b => b.Author).ToList();

    public static List<Blog> IncludePostsThenCommentsNoTracking(DiffDb db)
        => db.Blogs.AsNoTracking().Include(b => b.Posts).ThenInclude(p => p.Comments).Include(b => b.Author).ToList();

    public static List<Post> IncludeBlogThenPosts(DiffDb db)
        => db.Posts.Include(p => p.Blog).ThenInclude(b => b.Posts).AsSplitQuery().Where(p => p.Id < 500).ToList();

    public static List<Post> IncludeBlogThenPostsNoTracking(DiffDb db)
        => db.Posts.AsNoTracking().Include(p => p.Blog).ThenInclude(b => b.Posts).AsSingleQuery().Where(p => p.Id < 500).ToList();

    // EF Core rejects the cycle Post -> Blog -> Posts without tracking, but allows it with identity resolution.
    public static List<Post> IncludeBlogThenPostsIdentityResolution(DiffDb db)
        => db.Posts.AsNoTrackingWithIdentityResolution().Include(p => p.Blog).ThenInclude(b => b.Posts).Where(p => p.Id < 500).ToList();

    public static List<Author> IncludeAuthorBlogsThenPosts(DiffDb db)
        => db.Authors.AsNoTracking().Include(a => a.Blogs!).ThenInclude(b => b.Posts).ToList();

    public static List<Blog> IncludePostsPaged(DiffDb db, int skip, int take)
        => db.Blogs.AsNoTracking().OrderBy(b => b.Name).Skip(skip).Take(take).Include(b => b.Posts).ToList();

    public static List<Blog> IncludePostsUnordered(DiffDb db) => db.Blogs.AsNoTracking().Include(b => b.Posts).Take(4).ToList();

    public static Blog IncludeFirst(DiffDb db) => db.Blogs.Include(b => b.Posts).OrderByDescending(b => b.Rating).First();

    public static Task<Blog> IncludeSingleAsync(DiffDb db, int id) => db.Blogs.Include(b => b.Posts).ThenInclude(p => p.Comments).SingleAsync(b => b.Id == id);

    public static Task<List<Blog>> IncludeWhereToListAsync(DiffDb db, int rating)
        => db.Blogs.AsNoTracking().Include(b => b.Author).Include(b => b.Posts).Where(b => b.Rating >= rating).ToListAsync();

    public static List<Blog> IncludeIgnoringFilters(DiffDb db)
        => db.Blogs.AsNoTracking().IgnoreQueryFilters().Include(b => b.Posts).ThenInclude(p => p.Comments).ToList();

    public static int IncludeCount(DiffDb db) => db.Blogs.Include(b => b.Posts).Count();

    public static List<Blog> IncludeEnumerated(DiffDb db)
    {
        var blogs = new List<Blog>();
        foreach (var blog in db.Blogs.AsNoTracking().Include(b => b.Posts).Where(b => b.IsActive))
        {
            blogs.Add(blog);
        }

        return blogs;
    }

    public static List<Blog> IncludeAfterTrackingBlog(DiffDb db)
    {
        var tracked = db.Blogs.Single(b => b.Id == 1);
        tracked.Name = "changed";
        return db.Blogs.Include(b => b.Posts).Where(b => b.Id <= 2).ToList();
    }

    public static Blog LoadGraphModifySave(DiffDb db)
    {
        using var transaction = db.Database.BeginTransaction();
        var blog = db.Blogs.Include(b => b.Posts).ThenInclude(p => p.Comments).Single(b => b.Id == 1);
        blog.Posts[0].Title = "renamed";
        blog.Posts.RemoveAt(blog.Posts.Count - 1);
        db.SaveChanges();
        return blog;
    }

#if NET10_0_OR_GREATER
    public static List<Blog> IgnoringTenantFilter(DiffDb db)
        => db.Blogs.AsNoTracking().IgnoreQueryFilters([DiffDb.TenantFilter]).OrderBy(b => b.Id).ToList();
#endif
}
