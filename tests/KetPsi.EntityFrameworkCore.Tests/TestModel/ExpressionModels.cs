using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

public enum GadgetStatus
{
    Draft,
    Active,
    Retired,
}

public enum GadgetGrade
{
    A,
    B,
    C,
}

public sealed class Maker
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class Shop
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>An entity with a column of each kind computed values combine.</summary>
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

    public bool IsActive { get; set; }

    public bool? IsFeatured { get; set; }

    public GadgetStatus Status { get; set; }

    public GadgetGrade Grade { get; set; }

    public int MakerId { get; set; }

    public Maker Maker { get; set; } = null!;

    public int? ShopId { get; set; }

    public Shop? Shop { get; set; }
}

/// <summary>Gadgets for computed values: sizes, precision, an enum stored as text, a required and an optional reference.</summary>
public sealed class GadgetContext : BindingContext
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Gadget>(b =>
        {
            b.Property(g => g.Name).HasMaxLength(50);
            b.Property(g => g.Code).HasMaxLength(10);
            b.Property(g => g.Price).HasPrecision(18, 2);
            b.Property(g => g.Cost).HasPrecision(18, 2);
            b.Property(g => g.Grade).HasConversion<string>().HasMaxLength(1);
        });
}
