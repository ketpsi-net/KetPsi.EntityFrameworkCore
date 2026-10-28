namespace KetPsi.EntityFrameworkCore.Differential.Tests;

/// <summary>
/// Rows chosen to separate correct from almost-correct SQL: null and non-null views, repeated ratings, every
/// status, names that differ only in case, deleted rows and another tenant's rows.
/// </summary>
internal static class SeedData
{
    private static readonly DateTime Base = new(2024, 1, 1, 8, 0, 0);

    public static IEnumerable<Blog> Blogs() =>
    [
        Blog(1, "alpha", 5, true, 120, Status.Published, Base, 10.50m),
        Blog(2, "Beta", 3, false, null, Status.Draft, Base.AddDays(1), 0m),
        Blog(3, "gamma", 5, true, 40, Status.Archived, Base.AddDays(2).AddTicks(1234567), 99.99m),
        Blog(4, "delta", 1, false, 0, Status.Published, Base.AddDays(3), 5m),
        Blog(5, "epsilon", 4, true, null, Status.Published, Base.AddDays(4), 12.25m),
        Blog(6, "Alpha", 2, true, 300, Status.Draft, Base.AddDays(5), 7.10m),
        Blog(7, "zeta", 4, false, 40, Status.Archived, Base.AddDays(6), 0.01m),
        Blog(8, "eta", 3, true, 75, Status.Published, Base.AddDays(7), 45m),
        Blog(9, "theta", 5, true, 10, Status.Published, Base.AddDays(8), 3m, isDeleted: true),
        Blog(10, "iota", 4, true, 50, Status.Draft, Base.AddDays(9), 8m, tenantId: 2),
        Blog(11, "kappa", 2, false, null, Status.Archived, Base.AddDays(10), 1m, isDeleted: true, tenantId: 2),
    ];

    // Several blogs share an author, some have none, and author 4 has no blogs.
    private static readonly Dictionary<int, int> BlogAuthors = new() { [1] = 1, [2] = 1, [3] = 2, [5] = 2, [6] = 1, [8] = 3, [9] = 1, [10] = 2 };

    public static IEnumerable<Author> Authors() =>
    [
        new() { Id = 1, Name = "Ann" },
        new() { Id = 2, Name = "Bob" },
        new() { Id = 3, Name = "Cid" },
        new() { Id = 4, Name = "Dee" },
    ];

    // Keys out of insertion order, soft-deleted posts, a post of a soft-deleted blog and one of the other tenant.
    public static IEnumerable<Post> Posts() =>
    [
        Post(103, 1, "p103"),
        Post(101, 1, "p101"),
        Post(102, 1, "p102"),
        Post(104, 1, "p104", isDeleted: true),
        Post(201, 2, "p201"),
        Post(302, 3, "p302"),
        Post(301, 3, "p301"),
        Post(501, 5, "p501"),
        Post(502, 5, "p502", isDeleted: true),
        Post(801, 8, "p801"),
        Post(901, 9, "p901"),
        Post(1001, 10, "p1001"),
    ];

    public static IEnumerable<Comment> Comments() =>
    [
        Comment(2, 101, "c2"),
        Comment(1, 101, "c1"),
        Comment(3, 103, "c3"),
        Comment(4, 301, "c4"),
        Comment(5, 104, "c5"),
        Comment(6, 901, "c6"),
    ];

    public static Guid ExternalId(int id) => new($"00000000-0000-0000-0000-{id:D12}");

    public static int Revision(int id) => id * 10;

    private static Blog Blog(
        int id,
        string name,
        int rating,
        bool isActive,
        int? views,
        Status status,
        DateTime createdAt,
        decimal price,
        bool isDeleted = false,
        int tenantId = SqlServerFixture.Tenant)
        => new()
        {
            Id = id,
            Name = name,
            Rating = rating,
            IsActive = isActive,
            Views = views,
            Status = status,
            CreatedAt = createdAt,
            Price = price,
            ExternalId = ExternalId(id),
            IsDeleted = isDeleted,
            TenantId = tenantId,
            AuthorId = BlogAuthors.TryGetValue(id, out var authorId) ? authorId : null,
        };

    private static Post Post(int id, int blogId, string title, bool isDeleted = false)
        => new() { Id = id, BlogId = blogId, Title = title, IsDeleted = isDeleted };

    private static Comment Comment(int id, int postId, string text) => new() { Id = id, PostId = postId, Text = text };
}
