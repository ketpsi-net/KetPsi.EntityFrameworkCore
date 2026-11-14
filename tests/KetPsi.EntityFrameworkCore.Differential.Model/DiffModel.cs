using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KetPsi.EntityFrameworkCore.Differential;

public enum Status
{
    Draft,
    Published,
    Archived,
}

public sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Rating { get; set; }

    public bool IsActive { get; set; }

    public int? Views { get; set; }

    public Status Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Price { get; set; }

    public Guid ExternalId { get; set; }

    public bool IsDeleted { get; set; }

    public int TenantId { get; set; }

    public int? AuthorId { get; set; }

    public Author? Author { get; set; }

    public List<Post> Posts { get; set; } = [];
}

public sealed class Author
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public ICollection<Blog>? Blogs { get; set; }
}

public sealed class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public Blog Blog { get; set; } = null!;

    public string Title { get; set; } = "";

    public bool IsDeleted { get; set; }

    /// <summary>Without an initializer, so whoever loads the comments creates the collection.</summary>
    public ICollection<Comment>? Comments { get; set; }

    /// <summary>Many-to-many through <see cref="PostTag"/>.</summary>
    public List<Tag> Tags { get; set; } = [];
}

/// <summary>A tag of posts, whose filter hides hidden tags.</summary>
public sealed class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public bool IsHidden { get; set; }

    public List<Post> Posts { get; set; } = [];
}

/// <summary>The join entity class between posts and tags.</summary>
public sealed class PostTag
{
    public int PostId { get; set; }

    public int TagId { get; set; }
}

public sealed class Comment
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public Post Post { get; set; } = null!;

    public string Text { get; set; } = "";
}

/// <summary>A strongly typed key, stored as its number through a value converter.</summary>
public readonly record struct TicketId(int Value);

public enum Priority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// An entity whose properties go through value converters: a strongly typed key, enums stored as strings, bools stored
/// as 'Y' or 'N', a nullable number stored as text, a string stored upper case and a date read back as UTC.
/// </summary>
public sealed class Ticket
{
    public TicketId Id { get; set; }

    public string Code { get; set; } = "";

    public Priority Priority { get; set; }

    public Priority? Escalated { get; set; }

    public bool IsOpen { get; set; }

    public bool IsArchived { get; set; }

    public int? Points { get; set; }

    public DateTime DueAt { get; set; }

    public List<TicketNote> Notes { get; set; } = [];
}

public sealed class TicketNote
{
    public int Id { get; set; }

    public TicketId TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public string Text { get; set; } = "";
}

/// <summary>The context both query builds run on: stock EF Core and KetPsi.</summary>
public enum Shade
{
    Draft,
    Active,
    Retired,
}

/// <summary>A category, whose filter hides names starting with an underscore.</summary>
public sealed class GadgetCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Prefix { get; set; }

    public List<Gadget> Gadgets { get; set; } = [];
}

/// <summary>Columns of every kind computed values combine: nullable strings and numbers, short, long, double, decimal.</summary>
public sealed class Gadget
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Code { get; set; }

    public string? Note { get; set; }

    public decimal Price { get; set; }

    public decimal? Cost { get; set; }

    public int Quantity { get; set; }

    public int? Discount { get; set; }

    public short Stock { get; set; }

    public long Views { get; set; }

    public double Rating { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ShippedAt { get; set; }

    public bool IsActive { get; set; }

    public bool? IsFeatured { get; set; }

    public Shade Shade { get; set; }

    public int CategoryId { get; set; }

    public GadgetCategory Category { get; set; } = null!;
}

public sealed class DiffDb(string connectionString, int tenantId, CommandCounter? counter = null) : DbContext
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string TenantFilter = "Tenant";

    /// <summary>A shadow property, so tracked queries must carry values the entity class does not have.</summary>
    public const string Revision = "Revision";

    private readonly int _tenantId = tenantId;

    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<PostTag> PostTags => Set<PostTag>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketNote> TicketNotes => Set<TicketNote>();

    public DbSet<Gadget> Gadgets => Set<Gadget>();

    public DbSet<GadgetCategory> GadgetCategories => Set<GadgetCategory>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(connectionString);
        if (counter is not null)
        {
            optionsBuilder.AddInterceptors(counter);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>(b =>
        {
            b.ToTable("Blogs", "diff");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(50);
            b.Property(x => x.Price).HasPrecision(10, 2);
            b.Property<int>(Revision);
#if NET10_0_OR_GREATER
            b.HasQueryFilter(SoftDeleteFilter, x => !x.IsDeleted);
            b.HasQueryFilter(TenantFilter, x => x.TenantId == _tenantId);
#else
            b.HasQueryFilter(x => !x.IsDeleted && x.TenantId == _tenantId);
#endif
            b.HasOne(x => x.Author).WithMany(a => a.Blogs).HasForeignKey(x => x.AuthorId);
        });

        modelBuilder.Entity<Author>(a =>
        {
            a.ToTable("Authors", "diff");
            a.Property(x => x.Id).ValueGeneratedNever();
            a.Property(x => x.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<Post>(p =>
        {
            p.ToTable("Posts", "diff");
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Property(x => x.Title).HasMaxLength(50);
            p.HasOne(x => x.Blog).WithMany(b => b.Posts).HasForeignKey(x => x.BlogId);
            p.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Comment>(c =>
        {
            c.ToTable("Comments", "diff");
            c.Property(x => x.Id).ValueGeneratedNever();
            c.Property(x => x.Text).HasMaxLength(50);
            c.HasOne(x => x.Post).WithMany(p => p.Comments).HasForeignKey(x => x.PostId);
        });

        modelBuilder.Entity<Tag>(t =>
        {
            t.ToTable("Tags", "diff");
            t.Property(x => x.Id).ValueGeneratedNever();
            t.Property(x => x.Name).HasMaxLength(20);
            t.HasQueryFilter(x => !x.IsHidden);
            t.HasMany(x => x.Posts).WithMany(p => p.Tags).UsingEntity<PostTag>(
                right => right.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId),
                left => left.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId),
                join =>
                {
                    join.ToTable("PostTags", "diff");
                    join.HasKey(x => new { x.PostId, x.TagId });
                });
        });

        // A filter on a converted bool, so filters send the stored value too.
        modelBuilder.Entity<Ticket>(t =>
        {
            t.ToTable("Tickets", "diff");
            t.Property(x => x.Id).HasConversion(id => id.Value, value => new TicketId(value)).ValueGeneratedNever();
            t.Property(x => x.Code).HasConversion(v => v.ToUpperInvariant(), v => v.ToLowerInvariant()).HasMaxLength(10);
            t.Property(x => x.Priority).HasConversion<string>().HasMaxLength(10);
            t.Property(x => x.Escalated).HasConversion<string>().HasMaxLength(10);
            t.Property(x => x.IsOpen).HasConversion(new BoolToStringConverter("N", "Y")).HasMaxLength(1);
            t.Property(x => x.IsArchived).HasConversion(new BoolToStringConverter("N", "Y")).HasMaxLength(1);
            t.Property(x => x.Points).HasConversion(v => v.ToString(), v => int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture)).HasMaxLength(10);
            t.Property(x => x.DueAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            t.HasQueryFilter(x => !x.IsArchived);
        });

        modelBuilder.Entity<TicketNote>(n =>
        {
            n.ToTable("TicketNotes", "diff");
            n.Property(x => x.Id).ValueGeneratedNever();
            n.Property(x => x.TicketId).HasConversion(id => id.Value, value => new TicketId(value));
            n.Property(x => x.Text).HasMaxLength(50);
            n.HasOne(x => x.Ticket).WithMany(t => t.Notes).HasForeignKey(x => x.TicketId);
        });

        // A filter with a string method, so filters translate them too.
        modelBuilder.Entity<GadgetCategory>(c =>
        {
            c.ToTable("GadgetCategories", "diff");
            c.Property(x => x.Id).ValueGeneratedNever();
            c.Property(x => x.Name).HasMaxLength(20);
            c.Property(x => x.Prefix).HasMaxLength(10);
            c.HasQueryFilter(x => !x.Name.StartsWith("_"));
        });

        modelBuilder.Entity<Gadget>(g =>
        {
            g.ToTable("Gadgets", "diff");
            g.Property(x => x.Id).ValueGeneratedNever();
            g.Property(x => x.Name).HasMaxLength(50);
            g.Property(x => x.Code).HasMaxLength(10);
            g.Property(x => x.Note).HasMaxLength(20);
            g.Property(x => x.Price).HasPrecision(18, 2);
            g.Property(x => x.Cost).HasPrecision(18, 2);
            g.HasOne(x => x.Category).WithMany(c => c.Gadgets).HasForeignKey(x => x.CategoryId);
        });
    }
}

/// <summary>
/// Records the commands EF Core itself executes. KetPsi queries never go through EF's command pipeline, so with
/// KetPsi the only commands here are the ones SaveChanges sends.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private const string NoCountOff = "SET NOCOUNT OFF;";

    /// <summary>The number of queries EF Core ran, ExecuteDelete and stored procedures included.</summary>
    public int Reads { get; private set; }

    /// <summary>The text of every other command, such as the UPDATE SaveChanges sends, in order.</summary>
    public List<string> Writes { get; } = [];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    // ExecuteDelete runs as a query does, outside SaveChanges, and so does FromSql of a stored procedure. EF Core 11
    // sends ExecuteDelete after SET NOCOUNT OFF, so its row count is right on a server that has NOCOUNT on.
    private void Record(DbCommand command)
    {
        var text = command.CommandText.TrimStart();
        if (text.StartsWith(NoCountOff, StringComparison.OrdinalIgnoreCase))
        {
            text = text[NoCountOff.Length..].TrimStart();
        }

        if (text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("EXEC", StringComparison.OrdinalIgnoreCase))
        {
            Reads++;
        }
        else
        {
            Writes.Add(command.CommandText);
        }
    }
}
