using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(KetPsi.EntityFrameworkCore.Differential.Tests.SqlServerFixture))]

namespace KetPsi.EntityFrameworkCore.Differential.Tests;

/// <summary>One SQL Server container per test run, created and seeded through stock EF Core.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const int Tenant = 1;

    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var db = new DiffDb(ConnectionString, Tenant);
        await db.Database.EnsureCreatedAsync();
        foreach (var blog in SeedData.Blogs())
        {
            db.Blogs.Add(blog).Property<int>(DiffDb.Revision).CurrentValue = SeedData.Revision(blog.Id);
        }

        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
