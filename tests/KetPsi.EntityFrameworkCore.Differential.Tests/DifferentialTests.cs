extern alias Intercepted;
extern alias Stock;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential.Tests;

/// <summary>
/// Runs every query shape through stock EF Core and through KetPsi.EntityFrameworkCore against the same database,
/// and requires identical results: the same rows in the same order, the same scalars, the same exceptions.
/// </summary>
public sealed class DifferentialTests(SqlServerFixture fixture)
{
    private static readonly Type StockQueries = typeof(Stock::KetPsi.EntityFrameworkCore.Differential.Queries);
    private static readonly Type InterceptedQueries = typeof(Intercepted::KetPsi.EntityFrameworkCore.Differential.Queries);

    public static TheoryData<string, object?[]> Cases()
    {
        var cases = new TheoryData<string, object?[]>
        {
            { "RatingEquals", [5] },
            { "RatingNotEquals", [5] },
            { "RatingBelow", [3] },
            { "RatingAtMost", [3] },
            { "RatingAbove", [3] },
            { "RatingAtLeast", [4] },
            { "RatingAboveConstant", [] },
            { "ConstantOnLeft", [] },
            { "NotRatingBelow", [4] },
            { "NameEquals", ["alpha"] },
            { "NameEquals", [null] },
            { "NameNotEquals", ["alpha"] },
            { "NameNotEquals", [null] },
            { "NameEqualsMethod", ["gamma"] },
            { "CreatedAfter", [new DateTime(2024, 1, 3, 8, 0, 0).AddTicks(1234566)] },
            { "CreatedAfter", [new DateTime(2024, 1, 3, 8, 0, 0).AddTicks(1234567)] },
            { "PriceAtLeast", [10.50m] },
            { "ByExternalId", [SeedData.ExternalId(3)] },
            { "ViewsEquals", [40] },
            { "ViewsEquals", [null] },
            { "ViewsNotEquals", [40] },
            { "ViewsNotEquals", [null] },
            { "ViewsAbove", [40] },
            { "ViewsAbove", [null] },
            { "NotViewsAbove", [40] },
            { "NotViewsAbove", [null] },
            { "NotViewsEquals", [40] },
            { "NotViewsEquals", [null] },
            { "Active", [] },
            { "Inactive", [] },
            { "ActiveEquals", [false] },
            { "ViewsNull", [] },
            { "ViewsNotNull", [] },
            { "ViewsHasValue", [] },
            { "ViewsHasNoValue", [] },
            { "AndOr", [4] },
            { "NotAnd", [50] },
            { "NotOr", [3] },
            { "StatusIs", [Status.Archived] },
            { "NotDraft", [] },
            { "IdsIn", [new List<int> { 1, 3, 9, 10 }] },
            { "IdsIn", [new List<int>()] },
            { "IdsNotIn", [new[] { 2, 4 }] },
            { "IdsNotIn", [Array.Empty<int>()] },
            { "ViewsIn", [new List<int?> { 40, 120 }] },
            { "ViewsIn", [new List<int?> { 40, null }] },
            { "ViewsNotIn", [new List<int?> { 40, 120 }] },
            { "ViewsNotIn", [new List<int?> { 40, null }] },
            { "NamesIn", [new[] { "alpha", "eta" }] },
            { "StatusesIn", [new List<Status> { Status.Draft, Status.Archived }] },
            { "StaticField", [] },
            { "StaticArray", [] },
            { "PropertyChain", [4] },
            { "WhereIfName", ["Beta"] },
            { "WhereIfName", [null] },
            { "SeveralWheres", [4] },
            { "Composed", [4, true] },
            { "Composed", [null, false] },
            { "Composed", [null, true] },
            { "OrderByName", [] },
            { "OrderByRatingDescending", [] },
            { "OrderByNullableViews", [] },
            { "OrderByStatusThenNameDescending", [] },
            { "SecondOrderByReplaces", [] },
            { "Page", [2, 3] },
            { "Page", [0, 1] },
            { "Page", [20, 5] },
            { "SkipOnly", [5] },
            { "TakeOnly", [3] },
            { "TakeThenWhere", [] },
            { "TakeThenTake", [] },
            { "SkipThenSkip", [] },
            { "TakeThenOrderBy", [] },
            { "TakeThenSkip", [] },
            { "AllAsArray", [] },
            { "First", [] },
            { "FirstWhere", [4] },
            { "FirstNone", [] },
            { "FirstOrDefaultNone", [] },
            { "Single", [3] },
            { "SingleTooMany", [] },
            { "SingleNone", [] },
            { "SingleOrDefaultNone", [] },
            { "SingleOrDefaultTooMany", [] },
            { "Count", [] },
            { "CountWhere", [4] },
            { "LongCount", [] },
            { "CountAfterTake", [] },
            { "Any", [] },
            { "AnyWhere", [5] },
            { "AnyWhere", [4] },
            { "AnyAfterSkip", [3] },
            { "AnyAfterSkip", [30] },
            { "ToListAsync", [] },
            { "ToArrayAsync", [] },
            { "FirstAsync", [5] },
            { "FirstOrDefaultAsyncNone", [] },
            { "FirstAsyncNone", [] },
            { "SingleAsyncNone", [] },
            { "SingleAsync", [8] },
            { "SingleOrDefaultAsyncTooMany", [] },
            { "CountAsync", [3] },
            { "LongCountAsync", [] },
            { "AnyAsync", [4] },
            { "IgnoringFilters", [] },
            { "DeletedIgnoringFilters", [] },
            { "CountIgnoringFilters", [] },
            { "Tracked", [4] },
            { "AsTrackingAfterAsNoTracking", [] },
            { "AsTrackingWithBehavior", [QueryTrackingBehavior.TrackAll] },
            { "AsTrackingWithBehavior", [QueryTrackingBehavior.NoTracking] },
            { "AsTrackingWithBehavior", [QueryTrackingBehavior.NoTrackingWithIdentityResolution] },
            { "IdentityResolution", [] },
            { "ContextDefaultNoTracking", [] },
            { "TrackedFirst", [] },
            { "TrackedToListAsync", [] },
            { "TrackedSingleOrDefaultAsync", [5] },
            { "TrackedSingleOrDefaultAsync", [10] },
            { "RequeryKeepsTrackedValues", [] },
            { "LoadModifySave", [2] },
            { "LoadModifyShadowSaveAsync", [7] },
            { "LoadDeleteSave", [] },
            { "IncludePosts", [] },
            { "IncludePostsNoTracking", [] },
            { "IncludePostsIdentityResolution", [] },
            { "IncludeAuthor", [] },
            { "IncludeAuthorNoTracking", [] },
            { "IncludeAuthorIdentityResolution", [] },
            { "IncludeRequiredBlog", [] },
            { "IncludeRequiredBlogNoTracking", [] },
            { "IncludePostsThenComments", [] },
            { "IncludePostsThenCommentsNoTracking", [] },
            { "IncludeBlogThenPosts", [] },
            { "IncludeBlogThenPostsNoTracking", [] },
            { "IncludeBlogThenPostsIdentityResolution", [] },
            { "IncludeAuthorBlogsThenPosts", [] },
            { "IncludePostsPaged", [1, 3] },
            { "IncludePostsPaged", [0, 2] },
            { "IncludePostsUnordered", [] },
            { "IncludeFirst", [] },
            { "IncludeSingleAsync", [1] },
            { "IncludeSingleAsync", [2] },
            { "IncludeWhereToListAsync", [4] },
            { "IncludeIgnoringFilters", [] },
            { "IncludeCount", [] },
            { "IncludeEnumerated", [] },
            { "IncludeAfterTrackingBlog", [] },
            { "LoadGraphModifySave", [] },
            { "SelectAnonymous", [] },
            { "SelectRecord", [] },
            { "SelectInitializer", [] },
            { "SelectName", [] },
            { "SelectViews", [] },
            { "SelectTuple", [] },
            { "SelectNested", [] },
            { "SelectAuthorName", [] },
            { "SelectAuthorIdNullable", [] },
            { "SelectAuthorIdMissing", [] },
            { "SelectPostBlogName", [] },
            { "SelectTwoLevels", [] },
            { "SelectIgnoringFilters", [] },
            { "SelectPaged", [] },
            { "SelectTakeAfterSelect", [] },
            { "SelectFirst", [] },
            { "SelectFirstOrDefaultNone", [] },
            { "SelectSingleTooMany", [] },
            { "SelectSingleAsync", [3] },
            { "SelectToArrayAsync", [] },
            { "SelectCount", [] },
            { "SelectLongCountAsync", [] },
            { "SelectAny", [4] },
            { "SelectAny", [5] },
            { "SelectEnumerated", [] },
            { "SelectEntityAndColumn", [] },
            { "SelectEntityAndColumnNoTracking", [] },
            { "SelectAuthors", [] },
            { "SelectAuthorsNoTracking", [] },
            { "SelectAuthorsIdentityResolution", [] },
            { "SelectBlogAndAuthor", [] },
            { "SelectBlogAndAuthorNoTracking", [] },
            { "SelectBlogAndAuthorIdentityResolution", [] },
            { "SelectPostAndBlog", [] },
            { "SelectAuthorTwiceNoTracking", [] },
            { "SelectEntityWithInclude", [] },
            { "SelectNavigationWithInclude", [] },
            { "SelectEntityPaged", [] },
            { "SelectEntityFirst", [] },
            { "SelectComputed", ["!"] },
            { "SelectComputedCaptured", [10] },
            { "SelectComputedThroughMissingNavigation", [] },
            { "DistinctBlogs", [] },
            { "DistinctRatings", [] },
            { "DistinctAuthorNames", [] },
            { "DistinctCount", [] },
            { "DistinctAuthorsNoTracking", [] },
            { "DistinctAfterTake", [] },
            { "SumRating", [] },
            { "SumViews", [] },
            { "SumPrice", [] },
            { "AverageRating", [] },
            { "AverageViews", [] },
            { "AveragePrice", [] },
            { "MinName", [] },
            { "MaxCreatedAt", [] },
            { "MaxStatus", [] },
            { "MinViews", [] },
            { "MaxAuthorName", [] },
            { "MaxPostBlogRating", [] },
            { "SumNone", [] },
            { "MaxNone", [] },
            { "MaxNoneNullable", [] },
            { "AverageNone", [] },
            { "SelectSum", [] },
            { "SelectMaxAsync", [] },
            { "DistinctSum", [] },
            { "TakeThenSum", [] },
            { "SumAsync", [4] },
            { "MinAsyncNone", [] },
            { "GroupKeyAndCount", [] },
            { "GroupKeyOnly", [] },
            { "GroupAggregates", [] },
            { "GroupNullableKey", [] },
            { "GroupCompositeKey", [] },
            { "GroupCompositeKeyWhole", [] },
            { "GroupKeyThroughOptionalNavigation", [] },
            { "GroupKeyThroughRequiredNavigation", [] },
            { "GroupCountWithPredicate", [4] },
            { "GroupDistinctCount", [] },
            { "GroupComputed", ["!"] },
            { "GroupHaving", [2] },
            { "GroupHaving", [3] },
            { "GroupHavingOnKey", [] },
            { "GroupWhereAfterSelect", [2] },
            { "GroupOrderByKey", [] },
            { "GroupOrderByAggregate", [] },
            { "GroupOrderAfterSelect", [] },
            { "GroupPaged", [1, 2] },
            { "GroupPaged", [0, 10] },
            { "GroupFirst", [] },
            { "GroupCount", [] },
            { "GroupLongCountAsync", [] },
            { "GroupAny", [2] },
            { "GroupAny", [5] },
            { "GroupCountAfterSelect", [] },
            { "GroupDistinctAfterSelect", [] },
            { "GroupMaxOfCounts", [] },
            { "GroupWhereBefore", [] },
            { "GroupTakeBefore", [] },
            { "GroupIgnoringFilters", [] },
            { "GroupElementSelector", [] },
            { "GroupResultSelector", [] },
            { "GroupElementAndResultSelector", [] },
            { "GroupRecord", [] },
            { "GroupEmpty", [] },
            { "GroupToListAsync", [] },
            { "Groups", [] },
            { "GroupsNoTracking", [] },
            { "GroupsIdentityResolution", [] },
            { "GroupsNullableKey", [] },
            { "GroupsNullableReferenceKey", [] },
            { "GroupsNullableKeyElement", [] },
            { "GroupsElementSelector", [] },
            { "GroupsAsync", [] },
            { "GroupsCompositeKey", [] },
            { "GroupsAfterOrderBy", [] },
            { "GroupsWithInclude", [] },
            { "GroupsEnumerated", [] },

            // Last.
            { "LastByName", [] },
            { "LastOrDefaultBelow", [4] },
            { "LastOrDefaultBelow", [0] },
            { "LastOfPage", [] },
            { "LastAsyncAfterSkip", [] },
            { "LastProjected", [] },
            { "LastOrDefaultAsyncNone", [] },
            { "LastWithoutOrdering", [] },
            { "LastAfterDistinct", [] },

            // Reference navigations in predicates and orderings.
            { "PostsOfBlogNamed", ["alpha"] },
            { "PostsOfBlogNamed", ["theta"] },
            { "PostsOrderedByBlog", [3] },
            { "BlogsByAuthor", ["Ann"] },
            { "BlogsByAuthor", [null] },
            { "BlogsNotByAuthor", ["Ann"] },
            { "BlogsNotByAuthor", [null] },
            { "BlogsWithoutAuthor", [] },
            { "BlogsWithAuthor", [] },
            { "PostsWithBlog", [] },
            { "PostsOfBlogId", [1] },
            { "PostsOfBlogId", [9] },
            { "CountPostsOfActiveBlogs", [] },
            { "AnyCommentOnRatedBlog", [5] },
            { "AnyCommentOnRatedBlog", [6] },
            { "BlogsOrderedByAuthor", [] },
            { "PostsOfBlogNamedWithInclude", ["alpha"] },

            // Find.
            { "FindBlog", [1] },
            { "FindBlog", [9] },
            { "FindBlog", [999] },
            { "FindPostAsync", [101] },
            { "FindPostAsync", [104] },
            { "FindTrackedTwice", [3] },
            { "FindNull", [] },
            { "FindWrongType", [] },
            { "FindTwoValues", [] },

            // ExecuteDelete.
            { "DeleteCommentsOfPost", [101] },
            { "DeleteCommentsOfPost", [104] },
            { "DeleteCommentsThroughNavigation", ["alpha"] },
            { "DeleteCommentsThroughNavigation", ["theta"] },
            { "DeleteCommentsPaged", [] },
            { "DeleteFilteredPosts", [] },
            { "DeleteBlogsWithPosts", [] },
            { "DeleteTrackedComment", [] },

            // Join.
            { "JoinColumns", [] },
            { "JoinEntities", [] },
            { "JoinInnerFiltered", [4] },
            { "JoinThroughThePair", [3] },
            { "JoinTwice", [] },
            { "JoinCompositeKey", [] },
            { "JoinNullableKey", [] },
            { "JoinOuterKeyThroughNavigation", [] },
            { "JoinAfterPaging", [] },
            { "JoinPagedAfter", ["!"] },
            { "JoinCount", [] },
            { "JoinFirstAsync", [] },
            { "JoinIgnoringInnerFilters", [] },
            { "SelectEntitiesThenWhere", [2] },
            { "SelectColumnThenWhere", [3] },
            { "JoinCountThroughThePair", [4] },
            { "JoinCountThroughThePair", [9] },
            { "JoinAnyAsync", ["alpha"] },
            { "JoinAnyAsync", ["nobody"] },
            { "JoinFirstOrDefault", [3] },
            { "JoinFirstOrDefault", [10] },
            { "JoinFirstWithoutMatch", [] },
            { "SelectColumnSingle", [3] },
            { "SelectColumnSingleOfMany", [] },
            { "SelectColumnLongCount", [40] },
            { "SelectEntitiesLastAsync", [5] },
            { "SqlQueryRecordFirstOrDefault", [] },

            // FromSql.
            { "FromSqlUnderFilters", [4] },
            { "FromSqlReorderedColumns", [] },
            { "FromSqlTracked", [] },
            { "FromSqlMissingColumn", [] },
            { "FromSqlRunsAsWritten", [1] },
            { "FromSqlStoredProcedure", [] },
            { "FromSqlAsWrittenMissingColumn", [] },
            { "FromSqlAsWrittenTracked", [] },
            { "FromSqlComposed", [3] },
            { "FromSqlIgnoringFilters", [5] },
            { "FromSqlRawArguments", ["Beta", 100] },
            { "FromSqlDbParameter", [5, 20] },
            { "FromSqlInterpolatedWithComment", [3] },
            { "FromSqlNonComposable", [] },
            { "FromSqlInclude", [5] },
            { "FromSqlSelect", [] },
            { "FromSqlCount", [4] },
            { "FromSqlFirstAsync", [] },
            { "FromSqlGroupBy", [] },
            { "FromSqlJoinInner", [4] },
            { "FromSqlExecuteDelete", [101] },

            // Database.SqlQuery.
            { "SqlQueryFirstColumn", [] },
            { "SqlQueryComposed", [3] },
            { "SqlQueryWithoutValue", [] },
            { "SqlQueryNullable", [] },
            { "SqlQueryNullableCount", [40] },
            { "SqlQueryNullIntoInt", [] },
            { "SqlQueryRawAsync", [2] },
            { "SqlQueryFirstAsync", [] },
            { "SqlQueryEnumDistinct", [] },
            { "SqlQueryNonComposable", [] },
            { "SqlQueryRecord", [] },
            { "SqlQueryRecordComposed", [2] },
            { "SqlQueryClass", [4] },
            { "SqlQueryMissingColumn", [] },
            { "SqlQueryNullIntoProperty", [] },
        };

#if NET10_0_OR_GREATER
        cases.Add("IgnoringTenantFilter", []);
#endif
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Query_MatchesEfCore(string query, object?[] arguments)
    {
        var (stock, stockTracking, stockReads) = await RunAsync(StockQueries, query, arguments);
        var (intercepted, interceptedTracking, interceptedReads) = await RunAsync(InterceptedQueries, query, arguments);

#if NET8_0
        if (EfCore8NullSemantics.TryGetValue(Key(query, arguments), out var csharpPredicate))
        {
            // EF Core 8 translates these negations with SQL null semantics and drops rows with null Views; EF Core 9
            // and 10 follow C#, and so does KetPsi. Here the result is checked against C# instead of EF Core 8.
            Assert.NotEqual(stock, intercepted);
            stock = Describe(SeedData.Blogs().Where(b => !b.IsDeleted && b.TenantId == SqlServerFixture.Tenant).Where(csharpPredicate).ToList());
        }
#endif

        Assert.Equal(stock, intercepted);
        Assert.Equal(stockTracking, interceptedTracking);

        // Guards against comparing two empty trackers: these cases must track entities, and the saves must write.
        var key = Key(query, arguments);
        Assert.Equal(TrackingCases.Contains(key), !stockTracking.StartsWith("tracked:\n\nwritten:", StringComparison.Ordinal));
        Assert.Equal(query.StartsWith("Load", StringComparison.Ordinal), !stockTracking.EndsWith("written:\n", StringComparison.Ordinal));

        // EF Core ran the stock query; the intercepted one never reached EF Core's query pipeline.
        // A query EF Core rejects while compiling it, such as an include cycle without tracking, sends nothing.
        Assert.True(stockReads > 0 || stock.StartsWith("throws ", StringComparison.Ordinal), "Stock EF Core executed no query.");
        Assert.Equal(0, interceptedReads);
    }

    // The cases that leave entities in the change tracker; every other case must leave it empty.
    private static readonly HashSet<string> TrackingCases = new(StringComparer.Ordinal)
    {
        "Tracked(4)", "AsTrackingAfterAsNoTracking()", "AsTrackingWithBehavior(TrackAll)", "TrackedFirst()",
        "TrackedToListAsync()", "TrackedSingleOrDefaultAsync(5)", "RequeryKeepsTrackedValues()", "LoadModifySave(2)",
        "LoadModifyShadowSaveAsync(7)", "IncludePosts()", "IncludeAuthor()", "IncludeRequiredBlog()", "IncludePostsThenComments()",
        "IncludeBlogThenPosts()", "IncludeFirst()", "IncludeSingleAsync(1)", "IncludeSingleAsync(2)", "IncludeAfterTrackingBlog()",
        "LoadGraphModifySave()", "SelectEntityAndColumn()", "SelectAuthors()", "SelectBlogAndAuthor()", "SelectPostAndBlog()",
        "SelectNavigationWithInclude()", "SelectEntityFirst()", "Groups()",
        "FindBlog(1)", "FindPostAsync(101)", "FindTrackedTwice(3)", "DeleteTrackedComment()", "JoinEntities()",
        "FromSqlTracked()", "FromSqlAsWrittenTracked()",
    };

#if NET8_0
    private static readonly Dictionary<string, Func<Blog, bool>> EfCore8NullSemantics = new(StringComparer.Ordinal)
    {
        [Key("NotViewsAbove", [40])] = b => !(b.Views > 40),
        // In C#, a comparison with null is false, so its negation is true for every row.
        [Key("NotViewsAbove", [null])] = _ => true,
        [Key("NotAnd", [50])] = b => !(b.IsActive && b.Views > 50),
    };
#endif

    // Queries whose results have a defined order; other results are compared sorted by Id.
    private static readonly HashSet<string> OrderedQueries = new(StringComparer.Ordinal)
    {
        "Composed", "OrderByName", "OrderByRatingDescending", "OrderByNullableViews", "OrderByStatusThenNameDescending",
        "SecondOrderByReplaces", "Page", "SkipOnly", "TakeOnly", "TakeThenWhere", "TakeThenTake", "SkipThenSkip",
        "TakeThenOrderBy", "TakeThenSkip", "AllAsArray", "ToListAsync", "ToArrayAsync", "IgnoringFilters",
        "DeletedIgnoringFilters", "IgnoringTenantFilter", "IncludePostsPaged", "IncludePostsUnordered",
        "SelectAnonymous", "SelectPaged", "SelectTakeAfterSelect", "SelectToArrayAsync", "SelectAuthors", "SelectAuthorsNoTracking",
        "SelectAuthorsIdentityResolution", "SelectAuthorTwiceNoTracking", "SelectNavigationWithInclude", "SelectEntityPaged",
        "GroupOrderByKey", "GroupOrderByAggregate", "GroupOrderAfterSelect", "GroupPaged", "GroupElementAndResultSelector",
        "PostsOrderedByBlog", "BlogsOrderedByAuthor", "JoinThroughThePair", "JoinAfterPaging", "JoinPagedAfter", "SelectEntitiesThenWhere",
        "FromSqlComposed", "SqlQueryRawAsync", "SqlQueryRecordComposed",
    };

    private static string Key(string query, object?[] arguments)
        => query + "(" + string.Join(", ", arguments.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "null")) + ")";

    // Results are compared by their descriptions, so the description must show every projected member and instance.
    [Fact]
    public void Description_ShowsProjectedMembersAndSharedEntities()
    {
        var author = new Author { Id = 4, Name = "Dee" };
        var rows = new object?[]
        {
            new { Id = 1, Price = 2.50m, Author = author, Inner = new { Twice = author } },
            new Stock::KetPsi.EntityFrameworkCore.Differential.BlogSummary(2, "b", 3m),
            (3, (string?)null),
        };

        Assert.Equal(
            "roots {Id=1,Price=2.5,Author=@1,Inner={Twice=@1}},BlogSummary{Id=2,Name=b,Price=3},(3,null)\n@1 Author 4|Dee|blogs=null",
            Describe(rows));
    }

    [Fact]
    public void EveryCatalogQuery_HasACase()
    {
        var covered = Cases().Select(row => (string)row.Data.Item1).ToHashSet(StringComparer.Ordinal);
        var uncovered = StockQueries.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .Where(q => !covered.Contains(q))
            .ToList();

        Assert.True(uncovered.Count == 0, "Queries without a differential case: " + string.Join(", ", uncovered));
    }

    private async Task<(string Result, string Tracking, int Reads)> RunAsync(Type queries, string query, object?[] arguments)
    {
        var counter = new CommandCounter();
        await using var db = new DiffDb(fixture.ConnectionString, SqlServerFixture.Tenant, counter);

        // Only the query's own exceptions are results; describing the value runs outside, so a harness failure fails the
        // test instead of matching on both sides.
        object? value;
        try
        {
            value = queries.GetMethod(query)!.Invoke(null, [db, .. arguments]);
            if (value is Task task)
            {
                await task;
                value = task.GetType().GetProperty("Result")!.GetValue(task);
            }
        }
        catch (Exception exception) when (Unwrap(exception) is { } inner)
        {
            return ($"throws {inner.GetType().Name}: {inner.Message}", DescribeTracking(db, counter), counter.Reads);
        }

        if (value is IEnumerable rows and not string && !OrderedQueries.Contains(query))
        {
            var items = rows.Cast<object?>().ToList();
            value = items.TrueForAll(static r => r is Blog or Author or Post or Comment)
                ? items.OrderBy(r => KeyOf(r!)).ToList()
                : items.OrderBy(SortKey, StringComparer.Ordinal).ToList();
        }

        return (Describe(value), DescribeTracking(db, counter), counter.Reads);
    }

    // The change tracker after the query: every entry by type and key, with its state, modified properties, loaded
    // navigations, shadow value and values, then the commands SaveChanges sent. Stock EF Core and KetPsi must leave
    // the context alike.
    private static string DescribeTracking(DiffDb db, CommandCounter counter)
    {
        var entries = db.ChangeTracker.Entries()
            .OrderBy(e => e.Metadata.ClrType.Name, StringComparer.Ordinal)
            .ThenBy(e => KeyOf(e.Entity))
            .Select(e =>
            {
                var modified = string.Join(",", e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order(StringComparer.Ordinal));
                var loaded = string.Join(",", e.Navigations.Where(n => n.IsLoaded).Select(n => n.Metadata.Name).Order(StringComparer.Ordinal));
                var revision = e.Entity is Blog
                    ? string.Create(CultureInfo.InvariantCulture, $"|revision {e.Property(DiffDb.Revision).OriginalValue}->{e.Property(DiffDb.Revision).CurrentValue}")
                    : "";
                return $"{e.Metadata.ClrType.Name}|{e.State}{revision}|modified {modified}|loaded {loaded}|{Values(e.Entity)}";
            });

        return "tracked:\n" + string.Join("\n", entries) + "\nwritten:\n" + string.Join("\n", counter.Writes);
    }

    private static Exception? Unwrap(Exception exception)
        => exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;

    // Orders projected rows the same way on both sides: entities by type and key, every other value by its members.
    private static string SortKey(object? value) => value switch
    {
        null => "",
        Blog or Author or Post or Comment => value.GetType().Name + "#" + KeyOf(value).ToString("D6", CultureInfo.InvariantCulture),
        _ when IsScalar(value) => Scalar(value),
        System.Runtime.CompilerServices.ITuple tuple => string.Join("|", Enumerable.Range(0, tuple.Length).Select(i => SortKey(tuple[i]))),
        _ when GroupingKey(value) is { } grouping => "group|" + SortKey(grouping.Key),
        _ => string.Join("|", Members(value).Select(m => SortKey(m.Value))),
    };

    // An IGrouping's key and elements; the elements in a stable order, since the order within a group is undefined.
    private static (object? Key, List<object?> Elements)? GroupingKey(object value)
    {
        var grouping = value.GetType().GetInterfaces()
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IGrouping<,>));
        if (grouping is null)
        {
            return null;
        }

        var key = grouping.GetProperty(nameof(IGrouping<object, object>.Key))!.GetValue(value);
        return (key, [.. ((IEnumerable)value).Cast<object?>().OrderBy(SortKey, StringComparer.Ordinal)]);
    }

    private static bool IsScalar(object value)
        => value is string or bool or char or Enum or DateTime or DateTimeOffset or Guid or decimal || value.GetType().IsPrimitive;

    // Decimals are compared by value: the scale SQL Server returns depends on the expression that computed them.
    private static string Scalar(object value) => value switch
    {
        decimal d => d.ToString("0.############################", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static IEnumerable<(string Name, object? Value)> Members(object value)
        => value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.GetIndexParameters().Length == 0)
            .Select(p => (p.Name, p.GetValue(value)));

    private static int KeyOf(object entity) => entity switch
    {
        Blog blog => blog.Id,
        Author author => author.Id,
        Post post => post.Id,
        Comment comment => comment.Id,
        _ => throw new ArgumentException(entity.GetType().Name, nameof(entity)),
    };

    // A readable form of the result, so a mismatch shows exactly which rows or values differ. Entities are numbered
    // in the order they are reached, so the description also shows which instances are shared, how navigations
    // point at each other, which collection types hold them, and in what order.
    private static string Describe(object? value)
    {
        if (value is null or Blog or Author or Post or Comment)
        {
            return value is null ? "null" : new Graph().Describe([value]);
        }

        if (value is IEnumerable rows and not string)
        {
            return new Graph().Describe([.. rows.Cast<object?>()]);
        }

        return IsScalar(value) ? Scalar(value) + " (" + value.GetType().Name + ")" : new Graph().Describe([value]);
    }

    private static string Values(object entity) => entity switch
    {
        Blog blog => new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{blog.Id}|{blog.Name}|{blog.Rating}|{blog.IsActive}|{blog.Views?.ToString(CultureInfo.InvariantCulture) ?? "null"}")
            .Append(CultureInfo.InvariantCulture, $"|{blog.Status}|{blog.CreatedAt:O}|{blog.Price:0.00}|{blog.ExternalId}|{blog.IsDeleted}|{blog.TenantId}")
            .Append(CultureInfo.InvariantCulture, $"|{blog.AuthorId?.ToString(CultureInfo.InvariantCulture) ?? "null"}")
            .ToString(),
        Author author => string.Create(CultureInfo.InvariantCulture, $"{author.Id}|{author.Name}"),
        Post post => string.Create(CultureInfo.InvariantCulture, $"{post.Id}|{post.BlogId}|{post.Title}|{post.IsDeleted}"),
        Comment comment => string.Create(CultureInfo.InvariantCulture, $"{comment.Id}|{comment.PostId}|{comment.Text}"),
        _ => throw new ArgumentException(entity.GetType().Name, nameof(entity)),
    };

    /// <summary>Describes the entities reachable from the results, each once, numbered in the order they are reached.</summary>
    private sealed class Graph
    {
        private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Queue<object> _pending = new();

        public string Describe(IReadOnlyList<object?> roots)
        {
            var lines = new List<string> { "roots " + string.Join(",", roots.Select(Value)) };
            while (_pending.TryDequeue(out var entity))
            {
                lines.Add($"{Ref(entity)} {entity.GetType().Name} {Values(entity)}{Navigations(entity)}");
            }

            return string.Join("\n", lines);
        }

        private string Navigations(object entity) => entity switch
        {
            Blog blog => $"|author={Ref(blog.Author)}|posts={Collection(blog.Posts)}",
            Author author => $"|blogs={Collection(author.Blogs)}",
            Post post => $"|blog={Ref(post.Blog)}|comments={Collection(post.Comments)}",
            Comment comment => $"|post={Ref(comment.Post)}",
            _ => "",
        };

        // A projected value: entities by reference, so shared instances show; anything else by its members. Anonymous
        // types are named by neither side, since each assembly has its own.
        private string Value(object? value) => value switch
        {
            null => "null",
            Blog or Author or Post or Comment => Ref(value),
            _ when IsScalar(value) => Scalar(value),
            System.Runtime.CompilerServices.ITuple tuple => "(" + string.Join(",", Enumerable.Range(0, tuple.Length).Select(i => Value(tuple[i]))) + ")",
            _ when GroupingKey(value) is { } grouping => "group " + Value(grouping.Key) + "[" + string.Join(",", grouping.Elements.Select(Value)) + "]",
            _ => (value.GetType().Name.Contains("AnonymousType", StringComparison.Ordinal) ? "" : value.GetType().Name)
                + "{" + string.Join(",", Members(value).Select(m => m.Name + "=" + Value(m.Value))) + "}",
        };

        private string Ref(object? entity)
        {
            if (entity is null)
            {
                return "null";
            }

            if (!_ids.TryGetValue(entity, out var id))
            {
                _ids.Add(entity, id = _ids.Count + 1);
                _pending.Enqueue(entity);
            }

            return "@" + id.ToString(CultureInfo.InvariantCulture);
        }

        private string Collection<T>(IEnumerable<T>? items)
            where T : class
            => items is null ? "null" : items.GetType().Name + "[" + string.Join(",", items.Select(Ref)) + "]";
    }
}
