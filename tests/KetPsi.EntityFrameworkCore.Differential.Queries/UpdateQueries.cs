// ExecuteUpdate, run in a transaction that is rolled back: the rows updated and the table after the update. The same
// source compiles against EF Core 9, which takes the setters as an expression, and EF Core 10 and 11, which take a
// delegate; KetPsi translates the first at build time and records the second's SetProperty calls when it runs.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    public static object UpdateGadgetNames(DiffDb db)
        => Updated(db, () => db.Gadgets.Where(g => g.Quantity > 2).ExecuteUpdate(s => s.SetProperty(g => g.Name, "x")));

    public static object UpdateGadgetNotes(DiffDb db, string? note)
        => Updated(db, () => db.Gadgets.Where(g => g.Id <= 2).ExecuteUpdate(s => s.SetProperty(g => g.Note, note)));

    public static object UpdateGadgetsComputed(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s.SetProperty(g => g.Quantity, g => g.Quantity + 1).SetProperty(g => g.Price, g => g.Price * 2)));

    public static object UpdateGadgetsWithCaptures(DiffDb db, int bonus)
        => Updated(db, () => db.Gadgets.Where(g => g.Quantity >= bonus).ExecuteUpdate(s => s.SetProperty(g => g.Stock, g => (short)(g.Stock + bonus))));

    public static object UpdateGadgetsCoalesceAndConditional(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s
            .SetProperty(g => g.Note, g => g.Note ?? "none")
            .SetProperty(g => g.Code, g => g.Quantity > 5 ? "big" : "small")));

    public static object UpdateGadgetsStringMethods(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s.SetProperty(g => g.Name, g => g.Name.Trim().ToUpper() + "!")));

    public static object UpdateGadgetsDates(DiffDb db, int days)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s
            .SetProperty(g => g.CreatedAt, g => g.CreatedAt.AddDays(days))
            .SetProperty(g => g.ShippedAt, g => g.ShippedAt!.Value.AddHours(1.5))));

    public static object UpdateGadgetsMath(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s
            .SetProperty(g => g.Price, g => Math.Round(g.Price * 1.15m, 1))
            .SetProperty(g => g.Rating, g => Math.Floor(g.Rating) + Math.Abs(g.Rating - 3))));

    public static object UpdateGadgetsOfCategory(DiffDb db)
        => Updated(db, () => db.Gadgets.Where(g => g.Category.Name.StartsWith("B")).ExecuteUpdate(s => s.SetProperty(g => g.Quantity, 0)));

    public static object UpdateGadgetsPaged(DiffDb db)
        => Updated(db, () => db.Gadgets.OrderBy(g => g.Id).Skip(1).Take(2).ExecuteUpdate(s => s.SetProperty(g => g.Discount, g => g.Quantity)));

    public static object UpdateGadgetsTopByPrice(DiffDb db)
        => Updated(db, () => db.Gadgets.OrderByDescending(g => g.Price).ThenBy(g => g.Id).Take(2).ExecuteUpdate(s => s.SetProperty(g => g.Discount, (int?)null)));

    public static object UpdateGadgetShades(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s.SetProperty(g => g.Shade, g => g.IsActive ? Shade.Retired : Shade.Draft).SetProperty(g => g.IsActive, g => !g.IsActive)));

    public static object UpdateGadgetsNoRows(DiffDb db)
        => Updated(db, () => db.Gadgets.Where(g => g.Id > 1000).ExecuteUpdate(s => s.SetProperty(g => g.Name, "none")));

    public static object UpdateGadgetsSameColumnTwice(DiffDb db)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s => s.SetProperty(g => g.Quantity, 1).SetProperty(g => g.Quantity, 2)));

    public static Task<object> UpdateGadgetsAsync(DiffDb db)
        => UpdatedAsync(db, () => db.Gadgets.Where(g => g.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, "async")));

    public static object UpdateTrackedGadget(DiffDb db)
    {
        using var transaction = db.Database.BeginTransaction();
        try
        {
            var tracked = db.Gadgets.Single(g => g.Id == 1);
            var updated = db.Gadgets.ExecuteUpdate(s => s.SetProperty(g => g.Quantity, 99));
            return new { Updated = updated, Tracked = tracked.Quantity, Stored = db.Gadgets.AsNoTracking().Where(g => g.Id == 1).Select(g => g.Quantity).Single() };
        }
        finally
        {
            transaction.Rollback();
        }
    }

    public static object UpdateTicketsConverted(DiffDb db)
    {
        using var transaction = db.Database.BeginTransaction();
        try
        {
            var updated = db.Tickets.Where(t => t.Priority != Priority.High).ExecuteUpdate(s => s
                .SetProperty(t => t.Priority, Priority.High)
                .SetProperty(t => t.IsOpen, false)
                .SetProperty(t => t.Code, "upd"));
            return new { Updated = updated, Tickets = db.Tickets.IgnoreQueryFilters().AsNoTracking().OrderBy(t => t.Code).ToList() };
        }
        finally
        {
            transaction.Rollback();
        }
    }

#if NET10_0_OR_GREATER
    public static object UpdateGadgetsConditionally(DiffDb db, bool flag)
        => Updated(db, () => db.Gadgets.ExecuteUpdate(s =>
        {
            s.SetProperty(g => g.Stock, (short)7);
            if (flag)
            {
                s.SetProperty(g => g.Note, g => g.Name);
            }
        }));
#endif

    // The rows updated, then every gadget as the update left it.
    private static object Updated(DiffDb db, Func<int> update)
    {
        using var transaction = db.Database.BeginTransaction();
        try
        {
            var updated = update();
            return new { Updated = updated, Gadgets = db.Gadgets.AsNoTracking().OrderBy(g => g.Id).ToList() };
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static async Task<object> UpdatedAsync(DiffDb db, Func<Task<int>> update)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var updated = await update();
            return new { Updated = updated, Gadgets = await db.Gadgets.AsNoTracking().OrderBy(g => g.Id).ToListAsync() };
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
