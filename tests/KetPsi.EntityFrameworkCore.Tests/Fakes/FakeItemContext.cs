using System.Data;
using System.Data.Common;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Fakes;

/// <summary>Items over a <see cref="FakeDbConnection"/>, through EF Core's real SQL Server connection handling.</summary>
public sealed class FakeItemContext(FakeDbConnection connection, QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll)
    : DbContext
{
    public DbSet<Item> Items => Set<Item>();

    /// <summary>Registers what generated code would: the SQL Server dialect and the item reader.</summary>
    public static void EnsureRegistered()
    {
        SqlDialects.Register(SqlServerDialect.Instance);
        EntityReaders.Register(new ItemReader());
    }

    /// <summary>A result table with the item columns in the order the statements select them.</summary>
    public static DataTable Table(params Item[] items)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(Item.Id), typeof(int));
        table.Columns.Add(nameof(Item.IsActive), typeof(bool));
        table.Columns.Add(nameof(Item.Name), typeof(string));
        table.Columns.Add(nameof(Item.Score), typeof(int)).AllowDBNull = true;
        foreach (var item in items)
        {
            table.Rows.Add(item.Id, item.IsActive, item.Name, item.Score is { } score ? score : DBNull.Value);
        }

        return table;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(connection).UseQueryTrackingBehavior(tracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().ToTable("Items", "shop");

    /// <summary>A hand-written reader in the shape the generator emits. "Unmapped" tests the -1 ordinal.</summary>
    public sealed class ItemReader : IEntityReader<Item>
    {
        public IReadOnlyList<string> PropertyNames { get; } =
            [nameof(Item.Id), nameof(Item.IsActive), nameof(Item.Name), nameof(Item.Score), "Unmapped"];

        public Item Read(DbDataReader reader, ReadOnlySpan<int> ordinals)
        {
            var item = new Item
            {
                Id = reader.GetInt32(ordinals[0]),
                IsActive = reader.GetBoolean(ordinals[1]),
                Name = reader.GetString(ordinals[2]),
                Score = reader.IsDBNull(ordinals[3]) ? null : reader.GetInt32(ordinals[3]),
            };

            return ordinals[4] < 0 ? item : throw new InvalidOperationException("'Unmapped' must not be mapped.");
        }
    }
}
