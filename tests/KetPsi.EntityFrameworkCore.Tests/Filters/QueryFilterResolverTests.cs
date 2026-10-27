using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Filters;

public sealed class QueryFilterResolverTests
{
    public QueryFilterResolverTests()
    {
        TestQueryFilters.EnsureRegistered();
    }

    [Fact]
    public void ModelBinder_EntityWithoutFilters_HasNoFilterKeys()
    {
        using var db = new ItemContext();

        Assert.Empty(ModelBinder.GetEntity(db.Model, typeof(Item)).FilterKeys);
    }

    [Fact]
    public void ModelBinder_UnnamedFilter_HasNullKey()
    {
        using var db = new FilteredItemContext();

        Assert.Equal([null], ModelBinder.GetEntity(db.Model, typeof(Item)).FilterKeys);
    }

    [Fact]
    public void Resolve_EntityWithoutFilters_ReturnsNone()
    {
        using var db = new ItemContext();

        Assert.Empty(Resolve(db, IgnoredQueryFilters.None));
    }

    [Fact]
    public void Resolve_RegisteredFilter_ReturnsTranslation()
    {
        using var db = new FilteredItemContext();

        var filter = Assert.Single(Resolve(db, IgnoredQueryFilters.None));

        Assert.Null(filter.Key);
        Assert.Same(TestQueryFilters.Active, filter.Predicate);
        Assert.Empty(filter.Values);
    }

    [Fact]
    public void Resolve_ReadsValuesFromCurrentContext()
    {
        using var first = new TenantItemContext { TenantScore = 7 };
        using var second = new TenantItemContext { TenantScore = null };

        Assert.Equal([7], Assert.Single(Resolve(first, IgnoredQueryFilters.None)).Values);
        Assert.Equal([null], Assert.Single(Resolve(second, IgnoredQueryFilters.None)).Values);
    }

    [Fact]
    public void Resolve_IgnoreAll_SkipsFiltersEvenWithoutTranslation()
    {
        using var db = new UnregisteredFilterContext();

        Assert.Empty(Resolve(db, IgnoredFrom(db, SqlQuery.IgnoreQueryFilters(db.Set<Item>()))));
    }

    [Fact]
    public void Resolve_FilterWithoutTranslation_ThrowsNamingEntityFilterAndContext()
    {
        using var db = new UnregisteredFilterContext();

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(db, IgnoredQueryFilters.None));

        Assert.Contains(typeof(Item).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("(unnamed)", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(UnregisteredFilterContext).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ValueReaderReturnsWrongCount_Throws()
    {
        using var db = new MismatchFilterContext();

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(db, IgnoredQueryFilters.None));

        Assert.Contains("reads 1 value(s), but its value reader returned 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NullArguments_Throw()
    {
        using var db = new ItemContext();
        var entity = ModelBinder.GetEntity(db.Model, typeof(Item));

        Assert.Throws<ArgumentNullException>("context", () => QueryFilterResolver.Resolve(null!, entity, IgnoredQueryFilters.None));
        Assert.Throws<ArgumentNullException>("entity", () => QueryFilterResolver.Resolve(db, null!, IgnoredQueryFilters.None));
        Assert.Throws<ArgumentNullException>("ignored", () => QueryFilterResolver.Resolve(db, entity, null!));
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void ModelBinder_NamedFilters_HaveTheirKeys()
    {
        using var db = new NamedFilterItemContext();

        Assert.Equivalent(
            new[] { NamedFilterItemContext.ActiveFilter, NamedFilterItemContext.ScoredFilter },
            ModelBinder.GetEntity(db.Model, typeof(Item)).FilterKeys);
    }

    [Fact]
    public void Resolve_NamedFilters_ResolvesEach()
    {
        using var db = new NamedFilterItemContext();

        var filters = Resolve(db, IgnoredQueryFilters.None);

        Assert.Equivalent(
            new[] { NamedFilterItemContext.ActiveFilter, NamedFilterItemContext.ScoredFilter },
            filters.Select(f => f.Key));
    }

    [Fact]
    public void Resolve_IgnoreOneNamedFilter_KeepsTheOther()
    {
        using var db = new NamedFilterItemContext();
        var query = SqlQuery.IgnoreQueryFilters(db.Set<Item>(), [NamedFilterItemContext.ActiveFilter]);

        var filter = Assert.Single(Resolve(db, IgnoredFrom(db, query)));

        Assert.Equal(NamedFilterItemContext.ScoredFilter, filter.Key);
        Assert.Same(TestQueryFilters.ScoreNotNull, filter.Predicate);
    }
#endif

    private static IReadOnlyList<ResolvedQueryFilter> Resolve(DbContext db, IgnoredQueryFilters ignored)
        => QueryFilterResolver.Resolve(db, ModelBinder.GetEntity(db.Model, typeof(Item)), ignored);

    private static IgnoredQueryFilters IgnoredFrom(DbContext db, IQueryable<Item> query)
        => IgnoredQueryFilters.FromChain(((SqlQueryable<Item>)query).Tail);
}
