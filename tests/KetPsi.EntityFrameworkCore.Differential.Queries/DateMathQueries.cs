// Date and math functions in predicates, order keys and projections, which EF Core computes in SQL: date parts, the
// Add methods (DATEADD truncates a fractional amount), the database's clock, DATEDIFF, and Math's functions (ROUND
// rounds halves away from zero). A projection computes them in SQL too, as EF Core does.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    public static List<Gadget> GadgetsCreatedIn(DiffDb db, int year, int month)
        => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt.Year == year && g.CreatedAt.Month == month).ToList();

    public static List<Gadget> GadgetsCreatedAfterHour(DiffDb db, int hour)
        => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt.Hour >= hour || g.CreatedAt.Minute > 30 || g.CreatedAt.DayOfYear < 10).ToList();

    public static List<Gadget> GadgetsCreatedOn(DiffDb db, DateTime day)
        => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt.Date == day.Date).ToList();

    public static List<Gadget> GadgetsShippedWithinDays(DiffDb db, int days)
        => db.Gadgets.AsNoTracking().Where(g => g.ShippedAt!.Value <= g.CreatedAt.AddDays(days)).ToList();

    public static List<Gadget> GadgetsShippedInYear(DiffDb db, int year)
        => db.Gadgets.AsNoTracking().Where(g => g.ShippedAt!.Value.Year == year).ToList();

    public static List<Gadget> GadgetsShippedWithinFractionalDays(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.ShippedAt!.Value <= g.CreatedAt.AddDays(1.9)).ToList();

    public static List<Gadget> GadgetsCreatedBeforeNow(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => g.CreatedAt < DateTime.Now && g.CreatedAt < DateTime.UtcNow.AddYears(1) && g.CreatedAt <= DateTime.Today).ToList();

    public static List<Gadget> GadgetsShippedDaysLater(DiffDb db, int days)
        => db.Gadgets.AsNoTracking().Where(g => EF.Functions.DateDiffDay(g.CreatedAt, g.ShippedAt) >= days).ToList();

    public static List<int> GadgetsOrderedByMonthAndDay(DiffDb db)
        => db.Gadgets.OrderByDescending(g => g.CreatedAt.Month).ThenBy(g => g.CreatedAt.Day).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static List<Gadget> GadgetsWithAbsoluteRating(DiffDb db, double rating)
        => db.Gadgets.AsNoTracking().Where(g => Math.Abs(g.Rating - 3) <= rating && Math.Abs(g.Price - g.Cost!.Value) > 1m).ToList();

    public static List<Gadget> GadgetsRoundedTo(DiffDb db, decimal price)
        => db.Gadgets.AsNoTracking().Where(g => Math.Round(g.Price / 3, 1) >= price || Math.Round(g.Rating) == 2).ToList();

    public static List<Gadget> GadgetsFloorAndCeiling(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => Math.Floor(g.Rating) < Math.Ceiling(g.Rating) && decimal.Truncate(g.Price) == g.Price).ToList();

    public static List<Gadget> GadgetsWithPowerAndRoot(DiffDb db)
        => db.Gadgets.AsNoTracking().Where(g => Math.Pow(g.Rating, 2) > 5 || Math.Sqrt(g.Views) > 100 || Math.Log10(g.Quantity + 1) > 1).ToList();

    public static List<Gadget> GadgetsWithSign(DiffDb db, int sign)
        => db.Gadgets.AsNoTracking().Where(g => Math.Sign(g.Price - 10) == sign).ToList();

    public static List<int> GadgetsOrderedByDistanceFromTen(DiffDb db)
        => db.Gadgets.OrderBy(g => Math.Abs(g.Price - 10)).ThenBy(g => g.Id).Select(g => g.Id).ToList();

    public static object GadgetDateParts(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, g.CreatedAt.Year, g.CreatedAt.Month, g.CreatedAt.Day, g.CreatedAt.Hour, g.CreatedAt.DayOfYear, g.CreatedAt.Date }).ToList();

    public static object GadgetDatesAdded(DiffDb db, int days)
        => db.Gadgets.Select(g => new { g.Id, Plus = g.CreatedAt.AddDays(days), Half = g.CreatedAt.AddDays(1.5), Hours = g.CreatedAt.AddHours(2.5), Months = g.CreatedAt.AddMonths(1) }).ToList();

    public static object GadgetShippedParts(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, Year = (int?)g.ShippedAt!.Value.Year, Days = EF.Functions.DateDiffDay(g.CreatedAt, g.ShippedAt) }).ToList();

    public static object GadgetShippedYearsNonNullable(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, g.ShippedAt!.Value.Year }).ToList();

    public static object GadgetMath(DiffDb db)
        => db.Gadgets.Select(g => new
        {
            g.Id,
            Rounded = Math.Round(g.Price / 4),
            RoundedTo = Math.Round(g.Price / 3, 2),
            Truncated = Math.Truncate(g.Rating),
            Floor = Math.Floor(g.Price / 3),
            Ceiling = Math.Ceiling(g.Rating),
            Abs = Math.Abs(g.Quantity - 5),
            Power = Math.Pow(g.Rating, 2),
            Root = Math.Sqrt(g.Quantity),
        }).ToList();

    public static object GadgetRoundingHalves(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, Half = Math.Round(g.Price / 4 + 0.125m, 1), Whole = Math.Round(g.Rating + 0.5) }).ToList();

    public static object GadgetComputedMembersInSql(DiffDb db, string suffix)
        => db.Gadgets.Select(g => new { g.Id, Label = g.Name.Trim().ToUpper() + suffix, Total = g.Price * g.Quantity, Big = g.Quantity > 5, Note = g.Note ?? "none" }).ToList();

    public static List<int> DistinctGadgetYears(DiffDb db) => db.Gadgets.Select(g => g.CreatedAt.Year).Distinct().ToList();

#if NET11_0_OR_GREATER
    // EF Core 9 and 10 throw an InvalidCastException reading SIGN of a decimal or a double in a projection; EF Core 11 casts it.
    public static object GadgetSigns(DiffDb db)
        => db.Gadgets.Select(g => new { g.Id, Price = Math.Sign(g.Price - 10), Rating = Math.Sign(g.Rating - 3) }).ToList();
#endif
}
