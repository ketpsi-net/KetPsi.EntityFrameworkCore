using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public int? Stock { get; set; }

    public bool IsActive { get; set; }

    public Guid ExternalId { get; set; }

    public decimal Price { get; set; }
}

public abstract class Animal
{
    public int Id { get; set; }
}

public sealed class Dog : Animal
{
    public string Breed { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }

    public Address ShippingAddress { get; set; } = new();
}

public sealed class Customer
{
    public int Id { get; set; }

    public Address Address { get; set; } = new();
}

public sealed class Address
{
    public string City { get; set; } = "";
}

/// <summary>Base for the binding test contexts; each subclass has its own cached EF model.</summary>
public abstract class BindingContext : DbContext
{
    private const string UnusedConnectionString = "Server=unused;Database=unused";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(UnusedConnectionString);
}

/// <summary>Products with schema, renamed column, sizes, precision and a shadow property.</summary>
public sealed class ProductContext : BindingContext
{
    public const string CreatedAtShadowProperty = "CreatedAt";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products", "catalog");
            b.Property(p => p.Name).HasColumnName("ProductName").HasMaxLength(100);
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.Property<DateTime>(CreatedAtShadowProperty);
        });
}

/// <summary>Products with EF's default mapping.</summary>
public sealed class PlainProductContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>();
}

public sealed class ConverterContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>().Property(p => p.Name).HasConversion(v => v.ToUpperInvariant(), v => v);
}

public sealed class InheritanceContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Animal>();
        modelBuilder.Entity<Dog>();
    }
}

public sealed class OwnedContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress);
}

public sealed class ComplexContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Customer>().ComplexProperty(c => c.Address);
}

public sealed class ViewContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>().ToView("ProductsView");
}
