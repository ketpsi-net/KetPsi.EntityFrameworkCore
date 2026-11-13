using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

public sealed class Member
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Topic> Started { get; } = [];

    public List<Topic> Moderated { get; } = [];
}

public sealed class Forum
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public int? MinScore { get; set; }

    public List<Topic> Topics { get; } = [];

    public List<Follower> Followers { get; } = [];
}

public sealed class Topic
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public int Score { get; set; }

    public decimal? Rating { get; set; }

    public bool IsDeleted { get; set; }

    public int ForumId { get; set; }

    public Forum Forum { get; set; } = null!;

    public int StarterId { get; set; }

    public Member Starter { get; set; } = null!;

    public int? ModeratorId { get; set; }

    public Member? Moderator { get; set; }

    public List<Reply> Replies { get; } = [];

    public List<TopicLabel> TopicLabels { get; } = [];
}

public sealed class Reply
{
    public int Id { get; set; }

    public string Text { get; set; } = "";

    public int Likes { get; set; }

    public bool IsHidden { get; set; }

    public int TopicId { get; set; }
}

public sealed class Label
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class TopicLabel
{
    public int TopicId { get; set; }

    public int LabelId { get; set; }

    public Label Label { get; set; } = null!;
}

/// <summary>Follows a forum by its code, an alternate key.</summary>
public sealed class Follower
{
    public int Id { get; set; }

    public string ForumCode { get; set; } = "";
}

/// <summary>
/// Forums for collection navigations: required, optional and alternate keys, two navigations to the same entity, a
/// join entity, and topics and replies with query filters.
/// </summary>
public sealed class ForumContext : BindingContext
{
    public DbSet<Forum> Forums => Set<Forum>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Forum>(b =>
        {
            b.Property(f => f.Name).HasMaxLength(50);
            b.Property(f => f.Code).HasMaxLength(5);
            b.HasMany(f => f.Followers).WithOne().HasForeignKey(f => f.ForumCode).HasPrincipalKey(f => f.Code);
        });
        modelBuilder.Entity<Topic>(b =>
        {
            b.Property(t => t.Title).HasMaxLength(50);
            b.Property(t => t.Rating).HasPrecision(18, 2);
            b.HasOne(t => t.Starter).WithMany(m => m.Started).HasForeignKey(t => t.StarterId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.Moderator).WithMany(m => m.Moderated).HasForeignKey(t => t.ModeratorId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(t => t.Replies).WithOne().HasForeignKey(r => r.TopicId);
        });
        modelBuilder.Entity<TopicLabel>(b =>
        {
            b.HasKey(l => new { l.TopicId, l.LabelId });
            b.HasOne<Topic>().WithMany(t => t.TopicLabels).HasForeignKey(l => l.TopicId);
        });
        modelBuilder.Entity<Member>().Property(m => m.Name).HasMaxLength(50);
        modelBuilder.Entity<Label>().Property(l => l.Name).HasMaxLength(20);
        modelBuilder.Entity<Reply>().Property(r => r.Text).HasMaxLength(50);
        modelBuilder.Entity<Follower>().Property(f => f.ForumCode).HasMaxLength(5);
    }
}
