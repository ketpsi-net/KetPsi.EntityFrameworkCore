using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

public sealed class Writer
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public ICollection<Book>? Books { get; set; }

    public WriterProfile? Profile { get; set; }
}

public sealed class WriterProfile
{
    public int Id { get; set; }

    public int WriterId { get; set; }

    public Writer Writer { get; set; } = null!;
}

public sealed class Editor
{
    public int Id { get; set; }
}

public sealed class Book
{
    public int Id { get; set; }

    public int WriterId { get; set; }

    public Writer Writer { get; set; } = null!;

    public int? EditorId { get; set; }

    public Editor? Editor { get; set; }

    public List<Review> Reviews { get; set; } = [];

    public ICollection<Tag> Tags { get; set; } = [];
}

/// <summary>Related to its book through a shadow foreign key: there is no BookId property.</summary>
public sealed class Review
{
    public int Id { get; set; }

    public Book? Book { get; set; }
}

public sealed class Tag
{
    public int Id { get; set; }

    public ICollection<Book> Books { get; set; } = [];
}

public sealed class Section
{
    public int Floor { get; set; }

    public string Code { get; set; } = "";

    public List<Locker> Lockers { get; set; } = [];
}

/// <summary>Related to a section through a composite foreign key.</summary>
public sealed class Locker
{
    public int Id { get; set; }

    public int SectionFloor { get; set; }

    public string SectionCode { get; set; } = "";

    public Section Section { get; set; } = null!;
}

/// <summary>
/// Every navigation shape: collections, required and optional references, a reference without an inverse, a shadow
/// foreign key, one-to-one, a composite foreign key and many-to-many.
/// </summary>
public class LibraryContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Writer>().HasOne(w => w.Profile).WithOne(p => p.Writer).HasForeignKey<WriterProfile>(p => p.WriterId);
        modelBuilder.Entity<Book>().HasMany(b => b.Tags).WithMany(t => t.Books);
        modelBuilder.Entity<Book>().HasMany(b => b.Reviews).WithOne(r => r.Book);
        modelBuilder.Entity<Section>().HasKey(s => new { s.Floor, s.Code });
        modelBuilder.Entity<Locker>()
            .HasOne(l => l.Section)
            .WithMany(s => s.Lockers)
            .HasForeignKey(l => new { l.SectionFloor, l.SectionCode });
    }
}

/// <summary>The library with the writer's profile required, which makes the principal's reference required too.</summary>
public sealed class RequiredProfileLibraryContext : LibraryContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Writer>().Navigation(w => w.Profile).IsRequired();
    }
}
