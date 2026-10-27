using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure;

// The registry is process-wide, so every test registers under its own filter key.
public sealed class QueryFiltersTests
{
    private static readonly Func<DbContext, object?[]> NoValues = static _ => [];

    [Fact]
    public void Register_ThenFind_ReturnsRegistration()
    {
        var key = UniqueKey();
        var predicate = new BooleanPropertyTemplate(nameof(Item.IsActive));

        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, predicate, NoValues);

        var registration = QueryFilters.Find(typeof(RegistryBaseContext), typeof(Item), key);
        Assert.NotNull(registration);
        Assert.Same(predicate, registration.Predicate);
        Assert.Same(NoValues, registration.Values);
    }

    [Fact]
    public void Find_DerivedContext_FallsBackToBaseRegistration()
    {
        var key = UniqueKey();
        var predicate = new BooleanPropertyTemplate(nameof(Item.IsActive));
        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, predicate, NoValues);

        Assert.Same(predicate, QueryFilters.Find(typeof(RegistryDerivedContext), typeof(Item), key)?.Predicate);
    }

    [Fact]
    public void Find_PrefersMostDerivedRegistration()
    {
        var key = UniqueKey();
        var basePredicate = new BooleanPropertyTemplate(nameof(Item.IsActive));
        var derivedPredicate = new NullCheckTemplate(nameof(Item.Score), isNull: false);
        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, basePredicate, NoValues);
        QueryFilters.Register(typeof(RegistryDerivedContext), typeof(Item), key, derivedPredicate, NoValues);

        Assert.Same(derivedPredicate, QueryFilters.Find(typeof(RegistryDerivedContext), typeof(Item), key)?.Predicate);
        Assert.Same(basePredicate, QueryFilters.Find(typeof(RegistryBaseContext), typeof(Item), key)?.Predicate);
    }

    [Fact]
    public void Register_ForDbContext_AppliesToEveryContext()
    {
        var key = UniqueKey();
        var predicate = new BooleanPropertyTemplate(nameof(Item.IsActive));
        QueryFilters.Register(typeof(DbContext), typeof(Item), key, predicate, NoValues);

        Assert.Same(predicate, QueryFilters.Find(typeof(RegistryDerivedContext), typeof(Item), key)?.Predicate);
    }

    [Fact]
    public void Find_Unregistered_ReturnsNull()
        => Assert.Null(QueryFilters.Find(typeof(RegistryBaseContext), typeof(Item), UniqueKey()));

    [Fact]
    public void Find_OtherEntityType_ReturnsNull()
    {
        var key = UniqueKey();
        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, new BooleanPropertyTemplate(nameof(Item.IsActive)), NoValues);

        Assert.Null(QueryFilters.Find(typeof(RegistryBaseContext), typeof(Product), key));
    }

    [Fact]
    public void Register_SamePredicateAgain_IsIgnored()
    {
        var key = UniqueKey();
        var predicate = new BooleanPropertyTemplate(nameof(Item.IsActive));

        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, predicate, NoValues);
        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, predicate, static _ => [1]);

        Assert.Same(NoValues, QueryFilters.Find(typeof(RegistryBaseContext), typeof(Item), key)?.Values);
    }

    [Fact]
    public void Register_DifferentPredicateForSameFilter_ThrowsNamingFilter()
    {
        var key = UniqueKey();
        QueryFilters.Register(typeof(RegistryBaseContext), typeof(Item), key, new BooleanPropertyTemplate(nameof(Item.IsActive)), NoValues);

        var exception = Assert.Throws<InvalidOperationException>(() => QueryFilters.Register(
            typeof(RegistryBaseContext), typeof(Item), key, new BooleanPropertyTemplate(nameof(Item.IsActive)), NoValues));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Item).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_NotADbContext_Throws()
        => Assert.Throws<ArgumentException>("contextType", () => QueryFilters.Register(
            typeof(string), typeof(Item), UniqueKey(), new BooleanPropertyTemplate(nameof(Item.IsActive)), NoValues));

    [Fact]
    public void Register_EmptyKey_Throws()
        => Assert.Throws<ArgumentException>(() => QueryFilters.Register(
            typeof(RegistryBaseContext), typeof(Item), "", new BooleanPropertyTemplate(nameof(Item.IsActive)), NoValues));

    [Fact]
    public void Register_NullArguments_Throw()
    {
        var predicate = new BooleanPropertyTemplate(nameof(Item.IsActive));

        Assert.Throws<ArgumentNullException>("contextType", () => QueryFilters.Register(null!, typeof(Item), null, predicate, NoValues));
        Assert.Throws<ArgumentNullException>("entityType", () => QueryFilters.Register(typeof(DbContext), null!, null, predicate, NoValues));
        Assert.Throws<ArgumentNullException>("predicate", () => QueryFilters.Register(typeof(DbContext), typeof(Item), null, null!, NoValues));
        Assert.Throws<ArgumentNullException>("values", () => QueryFilters.Register(typeof(DbContext), typeof(Item), null, predicate, null!));
    }

    [Theory]
    [InlineData(null, "(unnamed)")]
    [InlineData("Active", "Active")]
    public void DisplayKey_NamesUnnamedFilters(string? key, string expected)
        => Assert.Equal(expected, QueryFilters.DisplayKey(key));

    private static string UniqueKey() => "Filter" + Guid.NewGuid().ToString("N");

    private class RegistryBaseContext : DbContext;

    private sealed class RegistryDerivedContext : RegistryBaseContext;
}
