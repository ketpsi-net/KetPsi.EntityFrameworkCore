// Computed values and string methods, compiled into both query builds like the rest of the catalog. The gadgets hold
// nulls in every nullable column, so each shape shows whether its null handling matches EF Core's.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    // Two properties.
    public static List<Gadget> GadgetsPriceAboveCost(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Price > g.Cost).ToList();

    public static List<Gadget> GadgetsNotPriceAboveCost(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !(g.Price > g.Cost)).ToList();

    public static List<Gadget> GadgetsQuantityEqualsDiscount(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Quantity == g.Discount).ToList();

    public static List<Gadget> GadgetsQuantityNotDiscount(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Quantity != g.Discount).ToList();

    public static List<Gadget> GadgetsCodeEqualsNote(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Code == g.Note).ToList();

    public static List<Gadget> GadgetsCodeNotNote(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Code != g.Note).ToList();

    public static List<Gadget> GadgetsNameEqualsCode(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name == g.Code).ToList();

    public static List<Gadget> GadgetsShippedAfterCreated(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.ShippedAt > g.CreatedAt).ToList();

    public static List<Gadget> GadgetsMixedWidths(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.Stock < g.Quantity || g.Views > g.Quantity && g.Rating > g.Quantity).ToList();

    public static List<Gadget> GadgetsCodeIsCategoryPrefix(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Code == g.Category.Prefix).ToList();

    public static List<Gadget> GadgetsFeaturedIsActive(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsFeatured == g.IsActive).ToList();

    // Arithmetic.
    public static List<Gadget> GadgetsTotalAbove(DiffDb db, decimal limit) => db.Gadgets.AsNoTracking().Where(g => g.Price * g.Quantity > limit).ToList();

    public static List<Gadget> GadgetsQuantityMinusAbove(DiffDb db, int quantity) => db.Gadgets.AsNoTracking().Where(g => g.Quantity - quantity > 0).ToList();

    public static List<Gadget> GadgetsIntegerOperators(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.Quantity % 2 == 0 || g.Quantity / 2 == 3 || (g.Quantity & 1) == 1 && (g.Quantity ^ 1) != 13).ToList();

    public static List<Gadget> GadgetsDecimalThird(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Price / 3 > 3).ToList();

    public static List<Gadget> GadgetsNegatedQuantity(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => -g.Quantity < -5 || ~g.Quantity == -1).ToList();

    public static List<Gadget> GadgetsDiscountPlusOneIs(DiffDb db, int? value) => db.Gadgets.AsNoTracking().Where(g => g.Discount + 1 == value).ToList();

    public static List<Gadget> GadgetsDiscountPlusOneIsNot(DiffDb db, int? value) => db.Gadgets.AsNoTracking().Where(g => g.Discount + 1 != value).ToList();

    public static List<Gadget> GadgetsMarginAbove(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Price - g.Cost > 4).ToList();

    public static List<Gadget> GadgetsNotMarginAbove(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !(g.Price - g.Cost > 4)).ToList();

    public static List<Gadget> GadgetsSquaredAboveViews(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Quantity * g.Quantity > g.Views).ToList();

    public static List<Gadget> GadgetsDividedByDiscount(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Quantity / g.Discount > 1).ToList();

    public static List<Gadget> GadgetsCasts(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => (int)g.Price == 8 || (int)g.Rating == 2 || (double)g.Quantity / 2 == 1.5 || (int)g.Shade == 2).ToList();

    // Concatenation.
    public static List<Gadget> GadgetsNamePlusCode(DiffDb db, string value) => db.Gadgets.AsNoTracking().Where(g => g.Name + g.Code == value).ToList();

    public static List<Gadget> GadgetsNamePlusQuantity(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name + "-" + g.Quantity == "Dune-12").ToList();

    public static List<Gadget> GadgetsCodePlusNoteIsNull(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Code + g.Note == null).ToList();

    // Values computed in C#.
    public static List<Gadget> GadgetsQuantityIsComputed(DiffDb db, int quantity) => db.Gadgets.AsNoTracking().Where(g => g.Quantity == quantity + 2).ToList();

    public static List<Gadget> GadgetsCreatedAfterNextDay(DiffDb db, DateTime day) => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt > day.AddDays(1)).ToList();

    public static List<Gadget> GadgetsQuantityInArray(DiffDb db)
    {
        var numbers = new[] { 3, 7 };
        return db.Gadgets.AsNoTracking().Where(g => g.Quantity == numbers[0] || g.Quantity == Math.Max(numbers[1], 5)).ToList();
    }

    public static List<Gadget> GadgetsBeforeNow(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt < DateTime.Now && g.Id < 3).ToList();

    public static List<Gadget> GadgetsWhenFlag(DiffDb db, bool flag) => db.Gadgets.AsNoTracking().Where(g => flag && g.IsActive).ToList();

    public static List<Gadget> GadgetsAlways(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => true).ToList();

    // Conditionals and coalescing.
    public static List<Gadget> GadgetsActivePriceAbove(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => (g.IsActive ? g.Price : 0) > 5).ToList();

    public static List<Gadget> GadgetsDiscountOrZero(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => (g.Discount ?? 0) > 0).ToList();

    public static List<Gadget> GadgetsCodeOrName(DiffDb db, string value) => db.Gadgets.AsNoTracking().Where(g => (g.Code ?? g.Name) == value).ToList();

    public static List<Gadget> GadgetsCodeOrNoteOrX(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => (g.Code ?? g.Note ?? "x") == "x").ToList();

    public static List<Gadget> GadgetsFeaturedOrFalse(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsFeatured ?? false).ToList();

    public static List<Gadget> GadgetsNotFeaturedOrFalse(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !(g.IsFeatured ?? false)).ToList();

    public static List<Gadget> GadgetsDiscountOrDefault(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Discount.GetValueOrDefault(5) == 5).ToList();

    public static List<Gadget> GadgetsTernaryCondition(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsActive ? g.Quantity > 5 : g.Quantity < 5).ToList();

    public static List<Gadget> GadgetsConditionIsActive(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => (g.Quantity > 5) == g.IsActive).ToList();

    public static List<Gadget> GadgetsActiveIsNoDiscount(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsActive == (g.Discount == null)).ToList();

    public static List<Gadget> GadgetsActiveXor(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsActive ^ (g.Quantity > 5)).ToList();

    public static List<Gadget> GadgetsNonShortCircuit(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.IsActive & g.Stock > 2 | g.Quantity == 3).ToList();

    public static List<Gadget> GadgetsNestedTernary(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => (g.Shade == Shade.Active ? 1 : g.Shade == Shade.Draft ? 2 : 3) == 1).ToList();

    public static List<Gadget> GadgetsCategoryNameLength(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Category.Name.Length == 5 && g.Quantity > 5).ToList();

    // String methods. SQL Server's collation ignores case, as with EF Core.
    public static List<Gadget> GadgetsNameContains(DiffDb db, string part) => db.Gadgets.AsNoTracking().Where(g => g.Name.Contains(part)).ToList();

    public static List<Gadget> GadgetsNoteContains(DiffDb db, string part) => db.Gadgets.AsNoTracking().Where(g => g.Note!.Contains(part)).ToList();

    public static List<Gadget> GadgetsNoteNotContains(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !g.Note!.Contains("o")).ToList();

    public static List<Gadget> GadgetsNameContainsCode(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name.Contains(g.Code!)).ToList();

    public static List<Gadget> GadgetsNoteNotContainsCode(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !g.Note!.Contains(g.Code!)).ToList();

    public static List<Gadget> GadgetsNameStartsWith(DiffDb db, string prefix) => db.Gadgets.AsNoTracking().Where(g => g.Name.StartsWith(prefix)).ToList();

    public static List<Gadget> GadgetsNoteNotStartsWith(DiffDb db, string prefix) => db.Gadgets.AsNoTracking().Where(g => !g.Note!.StartsWith(prefix)).ToList();

    public static List<Gadget> GadgetsNameEndsWith(DiffDb db, string suffix) => db.Gadgets.AsNoTracking().Where(g => g.Name.EndsWith(suffix)).ToList();

    public static List<Gadget> GadgetsNameStartsOrEndsWithCode(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.Name.StartsWith(g.Code!) || g.Name.EndsWith(g.Code!)).ToList();

#if NET10_0_OR_GREATER
    // EF Core 9 cannot translate the char overloads; EF Core 10 and later can, and KetPsi does on every version.
    public static List<Gadget> GadgetsNameContainsChar(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name.Contains('o') && !g.Name.StartsWith('D')).ToList();
#endif

    public static List<Gadget> GadgetsCaseFunctions(DiffDb db, string value)
        => db.Gadgets.AsNoTracking().Where(g => g.Name.ToUpper() == value || g.Name.ToLower() == value).ToList();

    public static List<Gadget> GadgetsTrimmed(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.Name.Trim() == "Halo" && g.Name.TrimStart() == "Halo " && g.Name.TrimEnd() == "  Halo").ToList();

    public static List<Gadget> GadgetsNameLengthAbove(DiffDb db, int length) => db.Gadgets.AsNoTracking().Where(g => g.Name.Length > length).ToList();

    public static List<Gadget> GadgetsShortTexts(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Code!.Length == 2 || g.Note!.Length == 0).ToList();

    public static List<Gadget> GadgetsNoteNullOrEmpty(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => string.IsNullOrEmpty(g.Note)).ToList();

    public static List<Gadget> GadgetsNoteNotNullOrEmpty(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !string.IsNullOrEmpty(g.Note)).ToList();

    public static List<Gadget> GadgetsNoteNullOrWhiteSpace(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => string.IsNullOrWhiteSpace(g.Note)).ToList();

    public static List<Gadget> GadgetsSubstrings(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name.Substring(0, 2) == "Du" || g.Name.Substring(1) == "oom").ToList();

    public static List<Gadget> GadgetsReplaced(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name.Replace("o", "0") == "Z0rk").ToList();

    public static List<Gadget> GadgetsIndexOf(DiffDb db, string part, int index) => db.Gadgets.AsNoTracking().Where(g => g.Name.IndexOf(part) == index).ToList();

    public static List<Gadget> GadgetsComparedNames(DiffDb db, string value)
        => db.Gadgets.AsNoTracking().Where(g => string.Compare(g.Name, value) > 0 && g.Name.CompareTo("Z") < 0).ToList();

    public static List<Gadget> GadgetsNameIs(DiffDb db, string value) => db.Gadgets.AsNoTracking().Where(g => g.Name == value).ToList();

    public static List<Gadget> GadgetsNumbersAsText(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Quantity.ToString() == "12" || g.Discount.ToString() == "").ToList();

    public static List<Gadget> GadgetsNameEqualsItsUpper(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => g.Name.Equals(g.Name.ToUpper()) && string.Equals(g.Code, g.Note) == false).ToList();

    public static List<Gadget> GadgetsConcatenated(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => string.Concat(g.Name, g.Code) == "DuneDU").ToList();

    // EF.Functions.Like.
    public static List<Gadget> GadgetsLike(DiffDb db, string pattern) => db.Gadgets.AsNoTracking().Where(g => EF.Functions.Like(g.Name, pattern)).ToList();

    public static List<Gadget> GadgetsLikeEscaped(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => EF.Functions.Like(g.Note, "%!%%", "!")).ToList();

    public static List<Gadget> GadgetsNotLike(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => !EF.Functions.Like(g.Note, "D%")).ToList();

    public static List<Gadget> GadgetsLikeCode(DiffDb db) => db.Gadgets.AsNoTracking().Where(g => EF.Functions.Like(g.Name, g.Code)).ToList();

    public static List<Gadget> GadgetsOfCategoryStartingWith(DiffDb db, string prefix)
        => db.Gadgets.AsNoTracking().Where(g => g.Category.Name.StartsWith(prefix)).ToList();

    public static List<GadgetCategory> GadgetCategoriesAll(DiffDb db) => db.GadgetCategories.AsNoTracking().ToList();

    // Computed order keys; the ids show the order.
    public static List<int> GadgetsOrderedByNameLength(DiffDb db) => db.Gadgets.OrderBy(g => g.Name.Length).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByTotal(DiffDb db) => db.Gadgets.OrderByDescending(g => g.Price * g.Quantity).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByCodeOrName(DiffDb db) => db.Gadgets.OrderBy(g => g.Code ?? g.Name).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByActive(DiffDb db)
        => db.Gadgets.OrderBy(g => g.IsActive ? 0 : 1).ThenByDescending(g => g.Quantity > 5).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByLowerName(DiffDb db) => db.Gadgets.OrderBy(g => g.Name.ToLower()).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByDiscount(DiffDb db) => db.Gadgets.OrderBy(g => g.Discount ?? 99).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByCategoryAndName(DiffDb db) => db.Gadgets.OrderBy(g => g.Category.Name + g.Name).Select(g => g.Id).ToList();

    public static List<int> GadgetsOrderedByVariable(DiffDb db, int key) => db.Gadgets.OrderBy(g => key).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<int> GadgetsPagedByTotal(DiffDb db)
        => db.Gadgets.OrderByDescending(g => g.Price * g.Quantity).ThenBy(g => g.Id).Take(3).Where(g => g.Id != 1).Select(g => g.Id).ToList();

    // Other operators.
    public static int CountGadgetsTotalAbove(DiffDb db, decimal limit) => db.Gadgets.Count(g => g.Price * g.Quantity > limit);

    public static Task<bool> AnyGadgetBelowCostAsync(DiffDb db) => db.Gadgets.AnyAsync(g => g.Price < g.Cost);

    public static int FirstGadgetStartingWith(DiffDb db, string prefix) => db.Gadgets.Where(g => g.Name.StartsWith(prefix)).Select(g => g.Id).First();

    public static int SumQuantityOfShortNames(DiffDb db) => db.Gadgets.Where(g => g.Name.Length == 4).Sum(g => g.Quantity);

    public static List<int> GadgetsAfterSelect(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, g.Name, g.Quantity }).Where(x => x.Quantity * 2 > 10 || x.Name.Contains("o")).Select(x => x.Id).ToList();

    public static List<int> GadgetsJoinedWithCategories(DiffDb db)
        => db.Gadgets
            .Join(db.GadgetCategories, g => g.CategoryId, c => c.Id, (g, c) => new { g.Id, g.Name, Category = c.Name })
            .Where(x => x.Name.Length == 4 && x.Category.StartsWith("G"))
            .Select(x => x.Id)
            .ToList();

    public static object GroupGadgetsWithShortNames(DiffDb db)
        => db.Gadgets.Where(g => g.Name.Length == 4).GroupBy(g => g.CategoryId).Select(gr => new { gr.Key, Count = gr.Count() }).ToList();

    public static object DeleteGadgetsComputed(DiffDb db)
        => InRolledBackTransaction(db, () => db.Gadgets.Where(g => g.Price * g.Quantity > 100 && g.Name.Contains("u")).ExecuteDelete());

    public static List<Gadget> GadgetsTrackedByUpperName(DiffDb db, string name) => db.Gadgets.Where(g => g.Name.ToUpper() == name).ToList();
}
