using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.TestModel;

/// <summary>Items with the unnamed filter <c>i => i.IsActive</c>.</summary>
public sealed class FilteredItemContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().ToTable("Items", "shop").HasQueryFilter(i => i.IsActive);
}

/// <summary>Items filtered by a value read from the context: <c>i => i.Score == TenantScore</c>.</summary>
public sealed class TenantItemContext : BindingContext
{
    public int? TenantScore { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().ToTable("Items", "shop").HasQueryFilter(i => i.Score == TenantScore);
}

/// <summary>A model filter that never gets a translation registered.</summary>
public sealed class UnregisteredFilterContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().HasQueryFilter(i => i.IsActive);
}

/// <summary>A filter whose registered value reader returns too few values.</summary>
public sealed class MismatchFilterContext : BindingContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>().HasQueryFilter(i => i.Score == 1);
}

#if NET10_0_OR_GREATER
/// <summary>Items with two EF Core 10 named filters.</summary>
public sealed class NamedFilterItemContext : BindingContext
{
    public const string ActiveFilter = "Active";
    public const string ScoredFilter = "Scored";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Item>(b =>
        {
            b.ToTable("Items", "shop");
            b.HasQueryFilter(ActiveFilter, i => i.IsActive);
            b.HasQueryFilter(ScoredFilter, i => i.Score != null);
        });
}
#endif

/// <summary>The translations the generator would emit for the filter test contexts.</summary>
public static class TestQueryFilters
{
    public static readonly PredicateTemplate Active = new BooleanPropertyTemplate(nameof(Item.IsActive));
    public static readonly PredicateTemplate ScoreEqualsValue = new ComparisonTemplate(nameof(Item.Score), ComparisonOperator.Equal, 0);
    public static readonly PredicateTemplate ScoreNotNull = new NullCheckTemplate(nameof(Item.Score), isNull: false);

    /// <summary>Registers every translation. Registration is idempotent, so each test can call this.</summary>
    public static void EnsureRegistered()
    {
        QueryFilters.Register(typeof(FilteredItemContext), typeof(Item), filterKey: null, Active, static _ => []);
        QueryFilters.Register(
            typeof(TenantItemContext), typeof(Item), filterKey: null, ScoreEqualsValue, static db => [((TenantItemContext)db).TenantScore]);
        QueryFilters.Register(typeof(MismatchFilterContext), typeof(Item), filterKey: null, ScoreEqualsValue, static _ => []);
#if NET10_0_OR_GREATER
        QueryFilters.Register(typeof(NamedFilterItemContext), typeof(Item), NamedFilterItemContext.ActiveFilter, Active, static _ => []);
        QueryFilters.Register(typeof(NamedFilterItemContext), typeof(Item), NamedFilterItemContext.ScoredFilter, ScoreNotNull, static _ => []);
#endif
    }
}
