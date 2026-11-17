// Projections that select values of collection navigations, computed in SQL, and the collections themselves, loaded
// with one more statement each. The related rows include soft-deleted posts, another tenant's blogs, a category without
// gadgets and keys behind converters, so each shape shows whether filters, empty collections and nulls match EF Core's.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    // Values of collections.
    public static object BlogPostCounts(DiffDb db)
        => db.Blogs.Select(b => new { b.Id, b.Posts.Count, Titled = b.Posts.Count(p => p.Title != "p103"), Long = b.Posts.LongCount() }).ToList();

    public static object BlogPostTests(DiffDb db)
        => db.Blogs.Select(b => new { b.Id, Any = b.Posts.Any(), All = b.Posts.All(p => p.Id > 101), Above = b.Posts.Any(p => p.Id % 10 > b.Rating) }).ToList();

    public static object BlogMaxPostId(DiffDb db) => db.Blogs.Select(b => new { b.Id, Max = b.Posts.Max(p => p.Id) }).ToList();

    public static object BlogNullableAggregates(DiffDb db)
        => db.Blogs.Select(b => new
        {
            b.Id,
            Max = (int?)b.Posts.Max(p => p.Id),
            Min = b.Posts.Min(p => (int?)p.Id),
            Average = b.Posts.Average(p => (double?)p.Id),
            Sum = b.Posts.Sum(p => p.Id),
        }).ToList();

    public static object CategoryAggregates(DiffDb db)
        => db.GadgetCategories.Select(c => new
        {
            c.Id,
            Total = c.Gadgets.Sum(g => g.Price * g.Quantity),
            MaxCost = c.Gadgets.Max(g => g.Cost),
            Rating = (double?)c.Gadgets.Average(g => g.Rating),
            Views = c.Gadgets.Sum(g => g.Views),
            Names = (string?)c.Gadgets.Min(g => g.Name),
        }).ToList();

    public static object BlogComputedFromCounts(DiffDb db, int bonus)
        => db.Blogs.Select(b => new { b.Id, Twice = b.Posts.Count() * 2 + bonus, Label = b.Name + ":" + b.Posts.Count }).ToList();

    public static object PostSiblingCounts(DiffDb db)
        => db.Posts.Select(p => new { p.Id, Siblings = p.Blog.Posts.Count(q => q.Id != p.Id) }).ToList();

    public static object BlogCountsPaged(DiffDb db)
        => db.Blogs.OrderBy(b => b.Id).Skip(1).Take(3).Select(b => new { b.Id, b.Posts.Count }).ToList();

    public static List<int> DistinctPostCounts(DiffDb db) => db.Blogs.Select(b => b.Posts.Count()).Distinct().ToList();

    public static object FirstBlogWithCount(DiffDb db) => db.Blogs.OrderBy(b => b.Id).Select(b => new { b.Name, Count = b.Posts.Count() }).First();

    public static int CountBlogsProjectedWithCounts(DiffDb db) => db.Blogs.Select(b => new { b.Id, Count = b.Posts.Count() }).Count();

    public static object BlogsWithCounts(DiffDb db) => db.Blogs.AsNoTracking().Select(b => new { b, b.Posts.Count }).ToList();

    public static object PostTagCounts(DiffDb db) => db.Posts.Select(p => new { p.Id, Tags = p.Tags.Count }).ToList();

    public static object TicketNoteCounts(DiffDb db) => db.Tickets.Select(t => new { t.Code, Notes = t.Notes.Count, Any = t.Notes.Any(n => n.Text != "") }).ToList();

    // Collections.
    public static object BlogsWithPostLists(DiffDb db) => db.Blogs.AsNoTracking().Select(b => new { b.Id, b.Posts }).ToList();

    public static object BlogPostTitles(DiffDb db)
        => db.Blogs.Select(b => new
        {
            b.Id,
            Titles = b.Posts.Where(p => p.Title != "p103").OrderByDescending(p => p.Title).Select(p => p.Title).ToList(),
        }).ToList();

    public static object BlogPostIdArrays(DiffDb db) => db.Blogs.Select(b => b.Posts.Select(p => p.Id).ToArray()).ToList();

    public static object BlogPostIdSequences(DiffDb db) => db.Blogs.Select(b => new { b.Id, Ids = b.Posts.Select(p => p.Id) }).ToList();

    public static object BlogPostSummaries(DiffDb db)
        => db.Blogs.Select(b => new
        {
            b.Id,
            Posts = b.Posts.OrderBy(p => p.Id).Select(p => new { p.Title, Blog = p.Blog.Name, Author = p.Blog.Author!.Name }).ToList(),
        }).ToList();

    public static object AuthorsWithBlogSets(DiffDb db) => db.Authors.AsNoTracking().Select(a => new { a.Id, a.Blogs }).ToList();

    public static object BlogsAndPostsTracked(DiffDb db) => db.Blogs.Select(b => new { b, b.Posts }).ToList();

    public static object BlogsAndPostsIdentityResolved(DiffDb db)
        => db.Blogs.AsNoTrackingWithIdentityResolution().Select(b => new { b, b.Posts }).ToList();

    public static object BlogPostsWithCaptures(DiffDb db, int min, int bonus)
        => db.Blogs.Select(b => new { b.Id, Ids = b.Posts.Where(p => p.Id > min).Select(p => p.Id + bonus).ToList() }).ToList();

    public static object BlogPostsPaged(DiffDb db)
        => db.Blogs.AsNoTracking().OrderBy(b => b.Id).Skip(1).Take(2).Select(b => new { b.Id, b.Posts }).ToList();

    public static object FirstBlogWithTitles(DiffDb db)
        => db.Blogs.OrderBy(b => b.Id).Select(b => new { b.Id, Titles = b.Posts.Select(p => p.Title).ToList() }).First();

    public static Task<List<List<string>>> BlogTitlesAsync(DiffDb db)
        => db.Blogs.Select(b => b.Posts.OrderBy(p => p.Title).Select(p => p.Title).ToList()).ToListAsync();

    public static object PostSiblingIds(DiffDb db)
        => db.Posts.Select(p => new { p.Id, Siblings = p.Blog.Posts.Where(q => q.Id != 0).Select(q => q.Id).ToList() }).ToList();

    public static object TicketNoteTexts(DiffDb db)
        => db.Tickets.Select(t => new { t.Code, Notes = t.Notes.OrderBy(n => n.Id).Select(n => n.Text).ToList() }).ToList();

    public static int CountBlogsProjectedWithPosts(DiffDb db) => db.Blogs.Select(b => new { b.Id, b.Posts }).Count();

    public static object BlogCommentedPostIds(DiffDb db)
        => db.Blogs.Select(b => new { b.Id, Ids = b.Posts.Where(p => p.Comments!.Any()).Select(p => p.Id).ToList() }).ToList();

    public static object CategoryGadgetNames(DiffDb db)
        => db.GadgetCategories.Select(c => new { c.Id, Names = c.Gadgets.Where(g => g.IsActive).OrderBy(g => g.Name).ThenBy(g => g.Id).Select(g => g.Name).ToArray(), c.Gadgets.Count }).ToList();
}
