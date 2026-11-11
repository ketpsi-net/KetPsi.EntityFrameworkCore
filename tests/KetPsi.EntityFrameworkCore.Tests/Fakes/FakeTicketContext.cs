using System.Data;
using System.Data.Common;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KetPsi.EntityFrameworkCore.Tests.Fakes;

public readonly record struct TicketId(int Value);

public enum TicketState
{
    Open,
    Closed,
}

public enum TicketKind
{
    Bug,
    Task,
}

/// <summary>An entity whose properties go through value converters, as EF Core configures them.</summary>
public sealed class Ticket
{
    public TicketId Id { get; set; }

    public string Code { get; set; } = "";

    public bool IsUrgent { get; set; }

    public TicketKind Kind { get; set; }

    public int? Points { get; set; }

    public TicketState? Previous { get; set; }

    public TicketState State { get; set; }

    public List<Note> Notes { get; } = [];
}

public sealed class Note
{
    public int Id { get; set; }

    public string Text { get; set; } = "";

    public TicketId TicketId { get; set; }

    public Ticket? Ticket { get; set; }
}

/// <summary>
/// Tickets over a <see cref="FakeDbConnection"/>: a strongly typed key, enums stored as strings, a bool stored as
/// 'Y' or 'N', a nullable number stored as a string, and a string stored upper case. Kind is an enum without a converter.
/// </summary>
public sealed class FakeTicketContext(FakeDbConnection connection, QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll)
    : DbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Note> Notes => Set<Note>();

    /// <summary>Registers what generated code would: the dialect, the readers and the navigation accessors.</summary>
    public static void EnsureRegistered()
    {
        SqlDialects.Register(SqlServerDialect.Instance);
        EntityReaders.Register(new TicketReader());
        EntityReaders.Register(new NoteReader());
        EntityNavigations.Register(new NotesAccessor());
        EntityNavigations.Register(new TicketAccessor());
    }

    /// <summary>A result table with the entity's columns in the order statements select them, holding stored values.</summary>
    public DataTable Table<T>(params Dictionary<string, object?>[] rows)
    {
        var entity = ModelBinder.GetEntity(Model, typeof(T));
        var table = new DataTable();
        foreach (var property in entity.Properties)
        {
            var stored = property.Converter?.ProviderClrType ?? property.ClrType;
            stored = Nullable.GetUnderlyingType(stored) ?? stored;
            table.Columns.Add(property.ColumnName, stored.IsEnum ? Enum.GetUnderlyingType(stored) : stored).AllowDBNull = true;
        }

        foreach (var row in rows)
        {
            table.Rows.Add([.. entity.Properties.Select(p => row.TryGetValue(p.Name, out var value) ? value ?? DBNull.Value : DBNull.Value)]);
        }

        return table;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(connection).UseQueryTrackingBehavior(tracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("Tickets");
            b.Property(t => t.Id).HasConversion(id => id.Value, value => new TicketId(value)).ValueGeneratedNever();
            b.Property(t => t.Code).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant()).HasMaxLength(10);
            b.Property(t => t.IsUrgent).HasConversion(new BoolToStringConverter("N", "Y")).HasMaxLength(1);
            b.Property(t => t.Points).HasConversion(v => v.ToString(), v => int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture));
            b.Property(t => t.Previous).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.State).HasConversion<string>().HasMaxLength(20);
            b.HasMany(t => t.Notes).WithOne(n => n.Ticket).HasForeignKey(n => n.TicketId);
        });
        modelBuilder.Entity<Note>(b =>
        {
            b.ToTable("Notes");
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.TicketId).HasConversion(id => id.Value, value => new TicketId(value));
        });
    }

    /// <summary>A hand-written reader in the shape the generator emits; it reads each column as the property's type.</summary>
    private sealed class TicketReader : IEntityReader<Ticket>
    {
        public IReadOnlyList<string> PropertyNames { get; } =
            [nameof(Ticket.Id), nameof(Ticket.Code), nameof(Ticket.IsUrgent), nameof(Ticket.Kind), nameof(Ticket.Points), nameof(Ticket.Previous), nameof(Ticket.State)];

        public Ticket Read(DbDataReader reader, ReadOnlySpan<int> ordinals) => new()
        {
            Id = reader.GetFieldValue<TicketId>(ordinals[0]),
            Code = reader.GetString(ordinals[1]),
            IsUrgent = reader.GetBoolean(ordinals[2]),
            Kind = (TicketKind)reader.GetInt32(ordinals[3]),
            Points = reader.IsDBNull(ordinals[4]) ? null : reader.GetInt32(ordinals[4]),
            Previous = reader.IsDBNull(ordinals[5]) ? null : (TicketState)reader.GetInt32(ordinals[5]),
            State = (TicketState)reader.GetInt32(ordinals[6]),
        };
    }

    private sealed class NoteReader : IEntityReader<Note>
    {
        public IReadOnlyList<string> PropertyNames { get; } = [nameof(Note.Id), nameof(Note.Text), nameof(Note.TicketId)];

        public Note Read(DbDataReader reader, ReadOnlySpan<int> ordinals) => new()
        {
            Id = reader.GetInt32(ordinals[0]),
            Text = reader.GetString(ordinals[1]),
            TicketId = reader.GetFieldValue<TicketId>(ordinals[2]),
        };
    }

    private sealed class NotesAccessor : INavigationAccessor
    {
        public Type EntityType => typeof(Ticket);

        public string NavigationName => nameof(Ticket.Notes);

        public void SetReference(object entity, object? value) => throw new NotSupportedException();

        public void EnsureCollection(object entity)
        {
        }

        public void AddToCollection(object entity, object item) => ((Ticket)entity).Notes.Add((Note)item);
    }

    private sealed class TicketAccessor : INavigationAccessor
    {
        public Type EntityType => typeof(Note);

        public string NavigationName => nameof(Note.Ticket);

        public void SetReference(object entity, object? value) => ((Note)entity).Ticket = (Ticket?)value;

        public void EnsureCollection(object entity) => throw new NotSupportedException();

        public void AddToCollection(object entity, object item) => throw new NotSupportedException();
    }
}
