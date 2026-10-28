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
}

public sealed class Comment
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public Post Post { get; set; } = null!;

    public string Text { get; set; } = "";
}

/// <summary>The context both query builds run on: stock EF Core and KetPsi.</summary>
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
    }
}

/// <summary>
/// Records the commands EF Core itself executes. KetPsi queries never go through EF's command pipeline, so with
/// KetPsi the only commands here are the ones SaveChanges sends.
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    /// <summary>The number of queries EF Core ran.</summary>
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

    private void Record(DbCommand command)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Reads++;
        }
        else
        {
            Writes.Add(command.CommandText);
        }
    }
}
