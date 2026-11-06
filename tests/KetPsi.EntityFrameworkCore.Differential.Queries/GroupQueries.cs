// GroupBy, compiled into both query builds like the rest of the catalog.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public sealed record StatusCount(Status Status, int Count);

public static partial class Queries
{
    // Keys and aggregates.
    public static object GroupKeyAndCount(DiffDb db) => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static List<int> GroupKeyOnly(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Select(g => g.Key).ToList();

    public static object GroupAggregates(DiffDb db) => db.Blogs.GroupBy(b => b.Status).Select(g => new
    {
        g.Key,
        SumRating = g.Sum(b => b.Rating),
        AverageRating = g.Average(b => b.Rating),
        AveragePrice = g.Average(b => b.Price),
        SumPrice = g.Sum(b => b.Price),
        FirstCreated = g.Min(b => b.CreatedAt),
        MaxViews = g.Max(b => b.Views),
        SumViews = g.Sum(b => b.Views),
        AverageViews = g.Average(b => b.Views),
        MinName = g.Min(b => b.Name),
        Long = g.LongCount(),
    }).ToList();

    public static object GroupNullableKey(DiffDb db) => db.Blogs.GroupBy(b => b.Views).Select(g => new { g.Key, Count = g.Count(), MaxRating = g.Max(b => b.Rating) }).ToList();

    public static object GroupCompositeKey(DiffDb db)
        => db.Blogs.GroupBy(b => new { b.Status, b.IsActive }).Select(g => new { g.Key.Status, g.Key.IsActive, Count = g.Count() }).ToList();

    public static object GroupCompositeKeyWhole(DiffDb db) => db.Blogs.GroupBy(b => new { b.Status, b.IsActive }).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupKeyThroughOptionalNavigation(DiffDb db) => db.Blogs.GroupBy(b => b.Author!.Name).Select(g => new { g.Key, Count = g.Count() }).ToList();

    // The blog is required and filtered: posts of a deleted blog are in no group, as in EF Core.
    public static object GroupKeyThroughRequiredNavigation(DiffDb db)
        => db.Posts.GroupBy(p => p.Blog.Name).Select(g => new { g.Key, Count = g.Count(), Titles = g.Select(p => p.Title).Distinct().Count() }).ToList();

    public static object GroupCountWithPredicate(DiffDb db, int rating)
        => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Rated = g.Count(b => b.Rating >= rating), Active = g.Where(b => b.IsActive).LongCount() }).ToList();

    public static object GroupDistinctCount(DiffDb db) => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Ratings = g.Select(b => b.Rating).Distinct().Count() }).ToList();

    public static object GroupComputed(DiffDb db, string suffix)
        => db.Blogs.GroupBy(b => b.Rating).Select(g => new { Label = g.Key + suffix, Twice = g.Count() * 2, Above = g.Sum(b => b.Rating) > Threshold }).ToList();

    // Where and OrderBy on the groups, before and after the Select.
    public static List<int> GroupHaving(DiffDb db, int min) => db.Blogs.GroupBy(b => b.Rating).Where(g => g.Count() >= min).Select(g => g.Key).ToList();

    public static object GroupHavingOnKey(DiffDb db)
        => db.Blogs.GroupBy(b => b.Views).Where(g => g.Key != 40 && g.Max(b => b.Rating) > 2).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupWhereAfterSelect(DiffDb db, int min)
        => db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, Count = g.Count() }).Where(x => x.Count >= min || x.Key == 1).ToList();

    public static object GroupOrderByKey(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).OrderBy(g => g.Key).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupOrderByAggregate(DiffDb db)
        => db.Blogs.GroupBy(b => b.Rating).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupOrderAfterSelect(DiffDb db)
        => db.Blogs.GroupBy(b => b.Status).Select(g => new { g.Key, Total = g.Sum(b => b.Price) }).OrderByDescending(x => x.Total).ThenBy(x => x.Key).ToList();

    public static object GroupPaged(DiffDb db, int skip, int take)
        => db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, Count = g.Count() }).OrderBy(x => x.Key).Skip(skip).Take(take).ToList();

    public static object GroupFirst(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Key).First();

    // Terminals on the groups and on their projection.
    public static int GroupCount(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Count();

    public static Task<long> GroupLongCountAsync(DiffDb db) => db.Blogs.GroupBy(b => new { b.Status, b.IsActive }).LongCountAsync();

    public static bool GroupAny(DiffDb db, int min) => db.Blogs.GroupBy(b => b.Rating).Any(g => g.Count() > min);

    public static int GroupCountAfterSelect(DiffDb db) => db.Blogs.GroupBy(b => b.Status).Select(g => g.Key).Count();

    public static List<int> GroupDistinctAfterSelect(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count()).Distinct().ToList();

    public static int GroupMaxOfCounts(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count()).Max();

    // Operators before GroupBy, and the other overloads.
    public static object GroupWhereBefore(DiffDb db) => db.Blogs.Where(b => b.IsActive).GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupTakeBefore(DiffDb db)
        => db.Blogs.OrderBy(b => b.Id).Take(4).GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupIgnoringFilters(DiffDb db) => db.Blogs.IgnoreQueryFilters().GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupElementSelector(DiffDb db) => db.Blogs.GroupBy(b => b.Status, b => b.Rating).Select(g => new { g.Key, Sum = g.Sum(), Max = g.Max() }).ToList();

    public static object GroupResultSelector(DiffDb db) => db.Blogs.GroupBy(b => b.Status, (key, blogs) => new { key, Count = blogs.Count() }).ToList();

    public static object GroupElementAndResultSelector(DiffDb db)
        => db.Blogs.GroupBy(b => b.Status, b => b.Price, (key, prices) => new { key, Total = prices.Sum() }).OrderBy(x => x.Total).ToList();

    public static List<StatusCount> GroupRecord(DiffDb db) => db.Blogs.GroupBy(b => b.Status).Select(g => new StatusCount(g.Key, g.Count())).ToList();

    public static object GroupEmpty(DiffDb db) => db.Blogs.Where(b => b.Rating > 100).GroupBy(b => b.Status).Select(g => new { g.Key, Max = g.Max(b => b.Rating) }).ToList();

    public static Task<List<int>> GroupToListAsync(DiffDb db) => db.Blogs.GroupBy(b => b.Rating).Select(g => g.Count()).ToListAsync();

    // The groups themselves: read ordered by the key and grouped in memory, with tracked elements.
    public static List<IGrouping<Status, Blog>> Groups(DiffDb db) => db.Blogs.GroupBy(b => b.Status).ToList();

    public static List<IGrouping<Status, Blog>> GroupsNoTracking(DiffDb db) => db.Blogs.AsNoTracking().GroupBy(b => b.Status).ToList();

    public static List<IGrouping<int, Blog>> GroupsIdentityResolution(DiffDb db) => db.Blogs.AsNoTrackingWithIdentityResolution().GroupBy(b => b.Rating).ToList();

    // EF Core cannot read a null into a nullable value-type key of groups returned directly, and throws; so does KetPsi.
    public static List<IGrouping<int?, Blog>> GroupsNullableKey(DiffDb db) => db.Blogs.AsNoTracking().GroupBy(b => b.AuthorId).ToList();

    public static List<IGrouping<string, Blog>> GroupsNullableReferenceKey(DiffDb db) => db.Blogs.AsNoTracking().GroupBy(b => b.Author!.Name).ToList();

    public static List<IGrouping<int?, int>> GroupsNullableKeyElement(DiffDb db) => db.Blogs.GroupBy(b => b.AuthorId, b => b.Id).ToList();

    public static List<IGrouping<Status, string>> GroupsElementSelector(DiffDb db) => db.Blogs.GroupBy(b => b.Status, b => b.Name).ToList();

    public static Task<IGrouping<int, Blog>[]> GroupsAsync(DiffDb db) => db.Blogs.AsNoTracking().GroupBy(b => b.Rating).ToArrayAsync();

    public static object GroupsCompositeKey(DiffDb db) => db.Blogs.AsNoTracking().GroupBy(b => new { b.Status, b.IsActive }).ToList();

    public static List<IGrouping<Status, Blog>> GroupsAfterOrderBy(DiffDb db) => db.Blogs.AsNoTracking().OrderBy(b => b.Name).GroupBy(b => b.Status).ToList();

    public static List<IGrouping<Status, Blog>> GroupsWithInclude(DiffDb db) => db.Blogs.AsNoTracking().Include(b => b.Author).GroupBy(b => b.Status).ToList();

    public static List<string> GroupsEnumerated(DiffDb db)
    {
        var groups = new List<string>();
        foreach (var group in db.Blogs.AsNoTracking().GroupBy(b => b.Rating))
        {
            groups.Add(group.Key + ":" + group.Count());
        }

        return groups;
    }
}
