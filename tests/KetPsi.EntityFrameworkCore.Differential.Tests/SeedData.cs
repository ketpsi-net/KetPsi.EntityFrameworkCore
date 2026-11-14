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

    // A hidden tag the filter removes, a tag on a soft-deleted post only, and a tag on no post.
    public static IEnumerable<Tag> Tags() =>
    [
        new() { Id = 1, Name = "news" },
        new() { Id = 2, Name = "tech" },
        new() { Id = 3, Name = "secret", IsHidden = true },
        new() { Id = 4, Name = "old" },
        new() { Id = 5, Name = "unused" },
    ];

    public static IEnumerable<PostTag> PostTags() =>
    [
        new() { PostId = 101, TagId = 1 },
        new() { PostId = 101, TagId = 2 },
        new() { PostId = 102, TagId = 2 },
        new() { PostId = 201, TagId = 3 },
        new() { PostId = 104, TagId = 4 },
        new() { PostId = 301, TagId = 1 },
        new() { PostId = 1001, TagId = 2 },
    ];

    // Every priority and none escalated, open and closed, points that order differently as text than as numbers, an
    // archived ticket the filter hides, and a ticket without notes.
    public static IEnumerable<Ticket> Tickets() =>
    [
        Ticket(1, "abc", Priority.High, Priority.High, true, false, 10),
        Ticket(2, "bcd", Priority.Low, null, false, false, 9),
        Ticket(3, "cde", Priority.Normal, Priority.High, true, false, null),
        Ticket(4, "def", Priority.High, null, false, false, 100),
        Ticket(5, "efg", Priority.Normal, Priority.Low, true, true, 7),
        Ticket(6, "fgh", Priority.Low, null, true, false, 2),
    ];

    public static IEnumerable<TicketNote> TicketNotes() =>
    [
        new() { Id = 12, TicketId = new(1), Text = "n12" },
        new() { Id = 11, TicketId = new(1), Text = "n11" },
        new() { Id = 31, TicketId = new(3), Text = "n31" },
        new() { Id = 41, TicketId = new(4), Text = "n41" },
        new() { Id = 51, TicketId = new(5), Text = "n51" },
    ];

    // A hidden category the filter removes, a category without a prefix, and one without gadgets.
    public static IEnumerable<GadgetCategory> GadgetCategories() =>
    [
        new() { Id = 1, Name = "Books", Prefix = "BK" },
        new() { Id = 2, Name = "Games", Prefix = null },
        new() { Id = 3, Name = "_Hidden", Prefix = "H" },
        new() { Id = 4, Name = "Toys", Prefix = "TY" },
    ];

    // Nulls in every nullable column, a price below its cost, a zero discount that divides by zero, wildcards in notes,
    // names with spaces and in another case, a shipping date before the creation date, and a long beyond int.
    public static IEnumerable<Gadget> Gadgets() =>
    [
        new() { Id = 1, Name = "Dune", Code = "DU", Note = "50%_off", Price = 10m, Cost = 6m, Quantity = 12, Discount = 2, Stock = 5, Views = 100, Rating = 4.5, CreatedAt = GadgetDay(1), ShippedAt = GadgetDay(3), IsActive = true, IsFeatured = true, Shade = Shade.Active, CategoryId = 1 },
        new() { Id = 2, Name = "Emma", Code = null, Note = "a[b]", Price = 8.5m, Cost = null, Quantity = 3, Discount = null, Stock = 0, Views = 0, Rating = 3.0, CreatedAt = GadgetDay(2), ShippedAt = null, IsActive = false, IsFeatured = null, Shade = Shade.Draft, CategoryId = 1 },
        new() { Id = 3, Name = "  Halo ", Code = "HA", Note = null, Price = 60m, Cost = 70m, Quantity = 1, Discount = 1, Stock = 1, Views = 5_000_000_000, Rating = 2.25, CreatedAt = GadgetDay(3), ShippedAt = new DateTime(2025, 12, 31), IsActive = true, IsFeatured = false, Shade = Shade.Retired, CategoryId = 2 },
        new() { Id = 4, Name = "Zork", Code = "ZORK", Note = "", Price = 0m, Cost = 0m, Quantity = 0, Discount = 0, Stock = 3, Views = 7, Rating = 0, CreatedAt = GadgetDay(4), ShippedAt = null, IsActive = true, IsFeatured = null, Shade = Shade.Active, CategoryId = 2 },
        new() { Id = 5, Name = "doom", Code = "do", Note = "Doom_2", Price = 20m, Cost = 5m, Quantity = 7, Discount = null, Stock = 7, Views = 3, Rating = 5, CreatedAt = GadgetDay(5), ShippedAt = new DateTime(2026, 2, 1), IsActive = false, IsFeatured = true, Shade = Shade.Active, CategoryId = 2 },
    ];

    public static DateTime GadgetDay(int day) => new(2026, 1, day);

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

    private static Ticket Ticket(int id, string code, Priority priority, Priority? escalated, bool isOpen, bool isArchived, int? points)
        => new()
        {
            Id = new(id),
            Code = code,
            Priority = priority,
            Escalated = escalated,
            IsOpen = isOpen,
            IsArchived = isArchived,
            Points = points,
            DueAt = new DateTime(2024, 2, id, 9, 30, 0, DateTimeKind.Utc),
        };
}
