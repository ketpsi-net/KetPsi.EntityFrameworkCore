using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

public sealed class Item
{
    public int Id { get; set; }

    public bool IsActive { get; set; }

    public string Name { get; set; } = "";

    public int? Score { get; set; }
}

/// <summary>A small model for exact SQL assertions: one nullable column, a schema.</summary>
public sealed class ItemContext : BindingContext
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().ToTable("Items", "shop");
}
