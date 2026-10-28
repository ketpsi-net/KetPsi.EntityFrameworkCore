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
        };
}
