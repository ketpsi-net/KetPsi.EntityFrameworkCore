// GroupBy on computed keys: date parts, comparisons, arithmetic, string methods, coalesce, captured variables, and
// anonymous keys that mix them with properties. EF Core computes the keys in a derived table; KetPsi groups by them.
namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    public static object GadgetsByCreatedYear(DiffDb db)
        => db.Gadgets.GroupBy(g => g.CreatedAt.Year).Select(g => new { g.Key, Count = g.Count(), Quantity = g.Sum(x => x.Quantity) }).ToList();

    public static object GadgetsByYearAndMonth(DiffDb db)
        => db.Gadgets.GroupBy(g => new { g.CreatedAt.Year, g.CreatedAt.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToList();

    public static object GadgetsByExpensive(DiffDb db)
        => db.Gadgets.GroupBy(g => g.Price > 10).Select(g => new { Expensive = g.Key, Count = g.Count(), Max = g.Max(x => x.Price) }).ToList();

    public static object GadgetsByQuantityBucket(DiffDb db)
        => db.Gadgets.GroupBy(g => g.Quantity / 5).Select(g => new { g.Key, Max = g.Max(x => x.Price), Min = g.Min(x => x.Name) }).ToList();

    public static object GadgetsByFirstLetter(DiffDb db)
        => db.Gadgets.GroupBy(g => g.Name.Trim().Substring(0, 1).ToUpper()).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GadgetsByShippedYear(DiffDb db)
        => db.Gadgets.GroupBy(g => g.ShippedAt!.Value.Year).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GadgetsByNullableShippedYear(DiffDb db)
        => db.Gadgets.GroupBy(g => (int?)g.ShippedAt!.Value.Year).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GadgetsByScaledQuantity(DiffDb db, int factor)
        => db.Gadgets.GroupBy(g => g.Quantity * factor).Where(g => g.Count() >= 1).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static List<int> YearsWithManyGadgets(DiffDb db)
        => db.Gadgets.GroupBy(g => g.CreatedAt.Year).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

    public static object GadgetMonthsPaged(DiffDb db)
        => db.Gadgets.GroupBy(g => g.CreatedAt.Day).OrderByDescending(g => g.Key).Select(g => new { g.Key, Count = g.Count() }).Skip(1).Take(2).ToList();

    public static int CountGadgetDays(DiffDb db) => db.Gadgets.GroupBy(g => g.CreatedAt.Day % 2).Count();

    public static object GadgetsByCategoryAndStocked(DiffDb db)
        => db.Gadgets.GroupBy(g => new { g.CategoryId, Stocked = g.IsActive && g.Stock > 0 })
            .Select(g => new { g.Key.CategoryId, g.Key.Stocked, Count = g.Count() })
            .ToList();

    public static object GadgetPricesByDiscount(DiffDb db)
        => db.Gadgets.GroupBy(g => g.Discount ?? 0, g => g.Price).Select(g => new { g.Key, Max = g.Max(), Sum = g.Sum() }).ToList();

    public static object GadgetsByRoundedPrice(DiffDb db)
        => db.Gadgets.GroupBy(g => Math.Round(g.Price / 10)).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GadgetsByCreatedDate(DiffDb db)
        => db.Gadgets.GroupBy(g => g.CreatedAt.Date).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GadgetsByCategoryName(DiffDb db)
        => db.Gadgets.GroupBy(g => g.Category.Name.ToUpper()).Select(g => new { g.Key, Count = g.Count() }).ToList();
}
