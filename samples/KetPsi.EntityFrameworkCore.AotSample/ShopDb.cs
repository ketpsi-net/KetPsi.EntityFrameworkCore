using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AotSample;

public enum Category
{
    Books,
    Games,
    Music,
}

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public decimal Price { get; set; }

    public int? Stock { get; set; }

    public bool IsActive { get; set; }

    public Category Category { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    /// <summary>A shadow property: tracked queries must hand its value to the change tracker.</summary>
    public const string Revision = "Revision";

    public DbSet<Product> Products => Set<Product>();

    public static DbContextOptions<ShopDb> Options(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ShopDb>().UseSqlServer(connectionString);
#if KETPSI_COMPILED_MODEL
        // Native AOT cannot build the model at runtime; eng/Update-CompiledModels.ps1 regenerates it.
        builder.UseModel(CompiledModel.ShopDbModel.Instance);
#endif
        return builder.Options;
    }

    // Compiled models cannot contain query filters, so this model has none.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products", "shop");
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Name).HasMaxLength(100);
            b.Property(p => p.Price).HasPrecision(10, 2);
            b.Property<int>(Revision);
        });
}

/// <summary>Lets <c>dotnet ef dbcontext optimize</c> create the context; the connection is never opened.</summary>
public sealed class ShopDbDesignTimeFactory : IDesignTimeDbContextFactory<ShopDb>
{
    private const string DesignTimeConnectionString = "Server=design-time;Database=design-time";

    public ShopDb CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ShopDb>().UseSqlServer(DesignTimeConnectionString).Options);
}
