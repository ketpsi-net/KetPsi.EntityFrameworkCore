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
        Assert.True(stockReads > 0, "Stock EF Core executed no query.");
        Assert.Equal(0, interceptedReads);
    }

    // The cases that leave entities in the change tracker; every other case must leave it empty.
    private static readonly HashSet<string> TrackingCases = new(StringComparer.Ordinal)
    {
        "Tracked(4)", "AsTrackingAfterAsNoTracking()", "AsTrackingWithBehavior(TrackAll)", "TrackedFirst()",
        "TrackedToListAsync()", "TrackedSingleOrDefaultAsync(5)", "RequeryKeepsTrackedValues()", "LoadModifySave(2)",
        "LoadModifyShadowSaveAsync(7)",
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
        "DeletedIgnoringFilters", "IgnoringTenantFilter",
    };

    private static string Key(string query, object?[] arguments)
        => query + "(" + string.Join(", ", arguments.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "null")) + ")";

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

        string result;
        try
        {
            var value = queries.GetMethod(query)!.Invoke(null, [db, .. arguments]);
            if (value is Task task)
            {
                await task;
                value = task.GetType().GetProperty("Result")!.GetValue(task);
            }

            if (value is IEnumerable<Blog> rows && !OrderedQueries.Contains(query))
            {
                value = rows.OrderBy(b => b.Id).ToList();
            }

            result = Describe(value);
        }
        catch (Exception exception) when (Unwrap(exception) is { } inner)
        {
            result = $"throws {inner.GetType().Name}: {inner.Message}";
        }

        return (result, DescribeTracking(db, counter), counter.Reads);
    }

    // The change tracker after the query, by key, with each entry's state, modified properties, shadow value and
    // entity values, then the commands SaveChanges sent. Stock EF Core and KetPsi must leave the context alike.
    private static string DescribeTracking(DiffDb db, CommandCounter counter)
    {
        var entries = db.ChangeTracker.Entries<Blog>()
            .OrderBy(e => e.Entity.Id)
            .Select(e =>
            {
                var revision = e.Property<int>(DiffDb.Revision);
                var modified = string.Join(",", e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order(StringComparer.Ordinal));
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{e.State}|revision {revision.OriginalValue}->{revision.CurrentValue}|modified {modified}|{Describe(e.Entity)}");
            });

        return "tracked:\n" + string.Join("\n", entries) + "\nwritten:\n" + string.Join("\n", counter.Writes);
    }

    private static Exception? Unwrap(Exception exception)
        => exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;

    // A readable form of the result, so a mismatch shows exactly which rows or values differ.
    private static string Describe(object? value) => value switch
    {
        null => "null",
        Blog blog => Describe(blog),
        IEnumerable rows and not string => string.Join("\n", rows.Cast<Blog>().Select(Describe)),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) + " (" + value.GetType().Name + ")",
    };

    private static string Describe(Blog blog)
        => new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{blog.Id}|{blog.Name}|{blog.Rating}|{blog.IsActive}|{blog.Views?.ToString(CultureInfo.InvariantCulture) ?? "null"}")
            .Append(CultureInfo.InvariantCulture, $"|{blog.Status}|{blog.CreatedAt:O}|{blog.Price:0.00}|{blog.ExternalId}|{blog.IsDeleted}|{blog.TenantId}")
            .ToString();
}
