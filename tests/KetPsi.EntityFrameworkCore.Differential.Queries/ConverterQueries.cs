// Properties with value converters, compiled into both query builds like the rest of the catalog. Ticket's key is a
// strongly typed TicketId, its enums are stored as strings, its bools as 'Y' or 'N', its points as text, and its
// filter hides archived tickets.
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public static partial class Queries
{
    public static List<Ticket> TicketsAll(DiffDb db) => db.Tickets.AsNoTracking().ToList();

    public static List<Ticket> TicketsTracked(DiffDb db) => db.Tickets.Where(t => t.Points != null).ToList();

    public static List<Ticket> TicketsIgnoringFilters(DiffDb db) => db.Tickets.IgnoreQueryFilters().AsNoTracking().ToList();

    // Values compared with converted properties are sent as the column stores them.
    public static List<Ticket> TicketById(DiffDb db, int id)
    {
        var ticketId = new TicketId(id);
        return db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).ToList();
    }

    public static List<Ticket> TicketsAfterId(DiffDb db, int id)
    {
        var (ticketId, first) = (new TicketId(id), new TicketId(1));
        return db.Tickets.AsNoTracking().Where(t => t.Id != ticketId && t.Id != first).ToList();
    }

    public static List<Ticket> TicketsByPriority(DiffDb db, Priority priority) => db.Tickets.AsNoTracking().Where(t => t.Priority == priority).ToList();

    public static List<Ticket> TicketsNotHigh(DiffDb db) => db.Tickets.AsNoTracking().Where(t => t.Priority != Priority.High).ToList();

    // Comparisons of a converted enum compare the stored strings: Low < Normal, not by number.
    public static List<Ticket> TicketsPriorityAbove(DiffDb db, Priority priority) => db.Tickets.AsNoTracking().Where(t => t.Priority > priority).ToList();

    public static List<Ticket> TicketsEscalated(DiffDb db, Priority? escalated) => db.Tickets.AsNoTracking().Where(t => t.Escalated == escalated).ToList();

    public static List<Ticket> TicketsOpen(DiffDb db) => db.Tickets.AsNoTracking().Where(t => t.IsOpen).ToList();

    public static List<Ticket> TicketsClosedOrHigh(DiffDb db) => db.Tickets.AsNoTracking().Where(t => !t.IsOpen || t.Priority == Priority.High).ToList();

    public static List<Ticket> TicketsOpenIs(DiffDb db, bool open) => db.Tickets.AsNoTracking().Where(t => t.IsOpen == open).ToList();

    // Points are stored as text, so 9 > 10 there, as in EF Core.
    public static List<Ticket> TicketsPointsAbove(DiffDb db, int points) => db.Tickets.AsNoTracking().Where(t => t.Points > points).ToList();

    public static List<Ticket> TicketsWithoutPoints(DiffDb db) => db.Tickets.AsNoTracking().Where(t => t.Points == null).ToList();

    public static List<Ticket> TicketsByCode(DiffDb db, string code) => db.Tickets.AsNoTracking().Where(t => t.Code == code).ToList();

    public static List<Ticket> TicketsByIds(DiffDb db, int first, int second)
    {
        var ids = new List<TicketId> { new(first), new(second) };
        return db.Tickets.AsNoTracking().Where(t => ids.Contains(t.Id)).ToList();
    }

    public static List<Ticket> TicketsByPriorities(DiffDb db)
    {
        var priorities = new[] { Priority.Low, Priority.High };
        return db.Tickets.AsNoTracking().Where(t => !priorities.Contains(t.Priority)).ToList();
    }

    public static List<Ticket> TicketsOrderedByPriority(DiffDb db)
        => db.Tickets.AsNoTracking().OrderBy(t => t.Priority).ThenByDescending(t => t.Id).ToList();

    public static List<Ticket> TicketsOrderedByPoints(DiffDb db) => db.Tickets.AsNoTracking().OrderBy(t => t.Points).ThenBy(t => t.Code).ToList();

    public static Ticket TicketFirstById(DiffDb db) => db.Tickets.AsNoTracking().OrderByDescending(t => t.Id).First();

    public static Ticket? FindTicket(DiffDb db, int id) => db.Tickets.Find(new TicketId(id));

    public static bool FindTicketTwice(DiffDb db, int id)
    {
        var ticketId = new TicketId(id);
        return ReferenceEquals(db.Tickets.Find(ticketId), db.Tickets.Single(t => t.Id == ticketId));
    }

    // Include matches the notes by the stored keys of the loaded tickets.
    public static List<Ticket> TicketsWithNotes(DiffDb db) => db.Tickets.AsNoTracking().Include(t => t.Notes).ToList();

    public static List<Ticket> TicketsWithNotesTracked(DiffDb db) => db.Tickets.Include(t => t.Notes).Where(t => t.IsOpen).ToList();

    public static List<TicketNote> NotesWithTicket(DiffDb db) => db.TicketNotes.AsNoTracking().Include(n => n.Ticket).ToList();

    public static List<TicketNote> NotesOfOpenTickets(DiffDb db) => db.TicketNotes.AsNoTracking().Where(n => n.Ticket.IsOpen && n.Ticket.Priority != Priority.Low).ToList();

    public static List<TicketNote> NotesOfTicket(DiffDb db, int id)
    {
        var ticketId = new TicketId(id);
        return db.TicketNotes.AsNoTracking().Where(n => n.TicketId == ticketId).ToList();
    }

    // Projections and group keys read the converted columns back.
    public static List<TicketId> SelectTicketIds(DiffDb db) => db.Tickets.Select(t => t.Id).ToList();

    public static object SelectTicketValues(DiffDb db)
        => db.Tickets.Select(t => new { t.Id, t.Priority, t.Escalated, t.IsOpen, t.Points, t.Code, t.DueAt }).ToList();

    public static List<Priority> SelectNotePriorities(DiffDb db) => db.TicketNotes.OrderBy(n => n.Id).Select(n => n.Ticket.Priority).ToList();

    public static List<Priority> DistinctPriorities(DiffDb db) => db.Tickets.Select(t => t.Priority).Distinct().ToList();

    public static object GroupTicketsByPriority(DiffDb db)
        => db.Tickets.GroupBy(t => t.Priority).Select(g => new { g.Key, Count = g.Count(), Open = g.Count(t => t.IsOpen) }).ToList();

    public static object GroupNotesByTicket(DiffDb db) => db.TicketNotes.GroupBy(n => n.TicketId).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object GroupTicketsHavingHigh(DiffDb db)
        => db.Tickets.GroupBy(t => t.Escalated).Where(g => g.Key == Priority.High).Select(g => new { g.Key, Max = g.Max(t => t.Points) }).ToList();

    public static int? MaxPoints(DiffDb db) => db.Tickets.Max(t => t.Points);

    public static Priority MinPriority(DiffDb db) => db.Tickets.Min(t => t.Priority);

    public static Task<TicketId> MaxTicketIdAsync(DiffDb db) => db.Tickets.MaxAsync(t => t.Id);

    public static int CountOpen(DiffDb db) => db.Tickets.Count(t => t.IsOpen);

    public static bool AnyEscalatedLow(DiffDb db) => db.Tickets.Any(t => t.Escalated == Priority.Low);

    public static object JoinNotesTickets(DiffDb db)
        => db.TicketNotes.Join(db.Tickets, n => n.TicketId, t => t.Id, (n, t) => new { n.Text, t.Priority, t.Id }).ToList();

    public static List<Ticket> TicketsFromSql(DiffDb db, int points)
        => db.Tickets.FromSql($"SELECT * FROM diff.Tickets WHERE Points <> {points.ToString(System.Globalization.CultureInfo.InvariantCulture)}").AsNoTracking().Where(t => t.IsOpen).ToList();

    public static List<TicketNote> NotesFromSqlAsWritten(DiffDb db) => db.TicketNotes.FromSql($"SELECT Text, TicketId, Id FROM diff.TicketNotes").AsNoTracking().ToList();

    public static object DeleteNotesOfTicket(DiffDb db, int id)
    {
        var ticketId = new TicketId(id);
        return InRolledBackTransaction(db, () => db.TicketNotes.Where(n => n.TicketId == ticketId).ExecuteDelete());
    }

    public static object DeleteNotesOfHighTickets(DiffDb db)
        => InRolledBackTransaction(db, () => db.TicketNotes.Where(n => n.Ticket.Priority == Priority.High).ExecuteDelete());

    public static Ticket LoadModifySaveTicket(DiffDb db, int id)
    {
        using var transaction = db.Database.BeginTransaction();
        var ticketId = new TicketId(id);
        var ticket = db.Tickets.Single(t => t.Id == ticketId);
        ticket.Priority = Priority.Low;
        ticket.IsOpen = !ticket.IsOpen;
        ticket.Points = null;
        db.SaveChanges();
        return ticket;
    }
}
