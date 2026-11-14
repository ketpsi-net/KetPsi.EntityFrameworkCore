// Queries over collection navigations, compiled into both query builds like the rest of the catalog. The related rows
// include soft-deleted posts, another tenant's blogs, hidden tags, keys behind converters and a category without
// gadgets, so each shape shows whether the query filters, correlations and empty collections match EF Core's.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    // Any and All.
    public static List<Blog> BlogsWithPosts(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any()).ToList();

    public static List<Blog> BlogsWithoutPosts(DiffDb db) => db.Blogs.AsNoTracking().Where(b => !b.Posts.Any()).ToList();

    public static List<Blog> BlogsWithPostTitled(DiffDb db, string title) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Title == title)).ToList();

    public static List<Blog> BlogsWithAllPostsAbove(DiffDb db, int id) => db.Blogs.AsNoTracking().Where(b => b.Posts.All(p => p.Id > id)).ToList();

    public static List<Blog> BlogsWithFilteredPosts(DiffDb db)
        => db.Blogs.AsNoTracking().Where(b => b.Posts.Where(p => p.Id > 101).Any(p => p.Title != "p103")).ToList();

    public static List<Blog> BlogsWithCommentedPosts(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Comments!.Any())).ToList();

    public static List<Blog> BlogsWithPostsAboveRating(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Id % 10 > b.Rating)).ToList();

    public static List<Blog> BlogsWithPostsIsActive(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any() == b.IsActive).ToList();

    public static List<Blog> BlogsWithPostsIf(DiffDb db, bool flag) => db.Blogs.AsNoTracking().WhereIf(flag, b => b.Posts.Any()).ToList();

    public static List<Post> PostsWithSiblings(DiffDb db) => db.Posts.AsNoTracking().Where(p => p.Blog.Posts.Any(q => q.Id != p.Id)).ToList();

    public static List<Post> PostsOfAuthorsWithOtherBlogs(DiffDb db)
        => db.Posts.AsNoTracking().Where(p => p.Blog.Author!.Blogs!.Any(b => b.Id != p.BlogId)).ToList();

    public static List<Author> AuthorsWithBlogs(DiffDb db) => db.Authors.AsNoTracking().Where(a => a.Blogs!.Any()).ToList();

    public static List<Author> AuthorsWithActiveBlogsOnly(DiffDb db) => db.Authors.AsNoTracking().Where(a => a.Blogs!.All(b => b.IsActive)).ToList();

    public static List<Author> AuthorsWithBlogsWithPosts(DiffDb db)
        => db.Authors.AsNoTracking().Where(a => a.Blogs!.Any(b => b.Posts.Any(p => p.Title.StartsWith("p1")))).ToList();

    // Counts.
    public static List<Blog> BlogsWithPostCount(DiffDb db, int count) => db.Blogs.AsNoTracking().Where(b => b.Posts.Count == count).ToList();

    public static List<Blog> BlogsWithMorePostsThan(DiffDb db, int count) => db.Blogs.AsNoTracking().Where(b => b.Posts.Count() > count).ToList();

    public static List<Blog> BlogsWithCommentedPostCount(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Count(p => p.Comments!.Any()) >= 1).ToList();

    public static List<Blog> BlogsWithLongPostCount(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.LongCount() == 2L).ToList();

    public static List<Blog> BlogsWithFilteredPostCount(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Where(p => p.Id > 101).Count() == 1).ToList();

    public static List<Blog> BlogsWithPostCountAboveRating(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Count * 2 > b.Rating).ToList();

    public static List<Ticket> TicketsWithNoteCount(DiffDb db, int count) => db.Tickets.AsNoTracking().Where(t => t.Notes.Count == count).ToList();

    public static List<int> AuthorsOrderedByBlogCount(DiffDb db)
        => db.Authors.OrderByDescending(a => a.Blogs!.Count).ThenBy(a => a.Id).Select(a => a.Id).ToList();

    public static List<int> BlogsOrderedByPostCount(DiffDb db)
        => db.Blogs.OrderByDescending(b => b.Posts.Count()).ThenBy(b => b.Id).Select(b => b.Id).ToList();

    public static List<int> BlogsPagedByPostCount(DiffDb db)
        => db.Blogs.OrderByDescending(b => b.Posts.Count()).ThenBy(b => b.Id).Skip(1).Take(3).Select(b => b.Id).ToList();

    public static int CountBlogsWithPosts(DiffDb db) => db.Blogs.Count(b => b.Posts.Any());

    public static Task<bool> AnyBlogWithPostCountAsync(DiffDb db, int count) => db.Blogs.AnyAsync(b => b.Posts.Count > count);

    public static List<Blog> BlogsWithPostsTracked(DiffDb db) => db.Blogs.Where(b => b.Posts.Count(p => p.Id > 101) > 1).ToList();

    public static List<int> BlogsJoinedWithAuthorsOfManyBlogs(DiffDb db)
        => db.Blogs
            .Join(db.Authors.Where(a => a.Blogs!.Count > 1), b => b.AuthorId, a => a.Id, (b, a) => new { b.Id, a.Name })
            .Select(x => x.Id)
            .ToList();

    public static object DeletePostsWithoutComments(DiffDb db)
        => InRolledBackTransaction(db, () => db.Posts.Where(p => !p.Comments!.Any() && !p.Tags.Any()).ExecuteDelete());

    // Aggregates. Sum of no rows is 0; Average, Min and Max of no rows are null, as in SQL.
    public static List<GadgetCategory> CategoriesWithQuantityAbove(DiffDb db, int quantity)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Sum(g => g.Quantity) > quantity).ToList();

    public static List<GadgetCategory> CategoriesWithDiscountSum(DiffDb db, int sum)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Sum(g => g.Discount) == sum).ToList();

    public static List<GadgetCategory> CategoriesWithAveragePriceAbove(DiffDb db, decimal price)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Average(g => g.Price) > price).ToList();

    public static List<GadgetCategory> CategoriesWithAverageQuantityAbove(DiffDb db, double quantity)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Average(g => g.Quantity) > quantity).ToList();

    public static List<GadgetCategory> CategoriesWithMaxRating(DiffDb db, double rating)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Max(g => g.Rating) == rating).ToList();

    public static List<GadgetCategory> CategoriesWithMaxQuantityNot(DiffDb db, int quantity)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Max(g => g.Quantity) != quantity).ToList();

    public static List<GadgetCategory> CategoriesWithMinCostBelow(DiffDb db, decimal cost)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Min(g => g.Cost) < cost).ToList();

    public static List<GadgetCategory> CategoriesWithSelectedMaxViews(DiffDb db, long views)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Select(g => g.Views).Max() > views).ToList();

    public static List<GadgetCategory> CategoriesWithViewSumAbove(DiffDb db, long views)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Sum(g => g.Views) > views).ToList();

    public static List<GadgetCategory> CategoriesWithTotalAbove(DiffDb db, decimal total)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Sum(g => g.Price * g.Quantity) > total).ToList();

    public static List<GadgetCategory> CategoriesWithActiveStock(DiffDb db, int stock)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Where(g => g.IsActive).Sum(g => g.Stock) == stock).ToList();

    public static List<GadgetCategory> CategoriesWithLongNamedGadgets(DiffDb db)
        => db.GadgetCategories.AsNoTracking().Where(c => c.Gadgets.Max(g => g.Name.Length) > c.Name.Length - 1).ToList();

    public static List<int> CategoriesOrderedByAverageRating(DiffDb db)
        => db.GadgetCategories.OrderByDescending(c => c.Gadgets.Average(g => g.Rating)).ThenBy(c => c.Id).Select(c => c.Id).ToList();

    public static List<Blog> BlogsWithMaxPostIdAbove(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Max(p => p.Id) > b.Id * 100 + 1).ToList();

    public static List<Blog> BlogsWithAveragePostIdAbove(DiffDb db, double id) => db.Blogs.AsNoTracking().Where(b => b.Posts.Average(p => p.Id) > id).ToList();

    // Many-to-many through the join entity class; the tags' filter applies inside.
    public static List<Post> PostsTagged(DiffDb db, string name) => db.Posts.AsNoTracking().Where(p => p.Tags.Any(t => t.Name == name)).ToList();

    public static List<Post> PostsWithTagCount(DiffDb db, int count) => db.Posts.AsNoTracking().Where(p => p.Tags.Count == count).ToList();

    public static List<int> TagsOnPosts(DiffDb db, int count) => db.Tags.Where(t => t.Posts.Count() >= count).Select(t => t.Id).ToList();

    public static List<Blog> BlogsWithTaggedPosts(DiffDb db) => db.Blogs.AsNoTracking().Where(b => b.Posts.Any(p => p.Tags.Any())).ToList();
}
