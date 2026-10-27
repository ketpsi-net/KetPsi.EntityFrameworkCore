using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
}

/// <summary>The context both query builds run on: stock EF Core and KetPsi.</summary>
public sealed class DiffDb(string connectionString, int tenantId, CommandCounter? counter = null) : DbContext
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string TenantFilter = "Tenant";

    private readonly int _tenantId = tenantId;

    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(connectionString);
        if (counter is not null)
        {
            optionsBuilder.AddInterceptors(counter);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Blog>(b =>
        {
            b.ToTable("Blogs", "diff");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(50);
            b.Property(x => x.Price).HasPrecision(10, 2);
#if NET10_0_OR_GREATER
            b.HasQueryFilter(SoftDeleteFilter, x => !x.IsDeleted);
            b.HasQueryFilter(TenantFilter, x => x.TenantId == _tenantId);
#else
            b.HasQueryFilter(x => !x.IsDeleted && x.TenantId == _tenantId);
#endif
        });
}

/// <summary>Counts the commands EF Core itself executes; KetPsi queries never go through EF's command pipeline.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Count { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count++;
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count++;
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(result);
    }
}
