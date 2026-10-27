using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

/// <summary>A context with a real provider so EF services resolve; its connection is never opened.</summary>
public sealed class TestDbContext : DbContext
{
    private const string UnusedConnectionString = "Server=unused;Database=unused";

    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(UnusedConnectionString);
}
