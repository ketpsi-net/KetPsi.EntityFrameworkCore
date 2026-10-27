using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Filters;

public sealed class IgnoredQueryFiltersTests : IDisposable
{
    private readonly ItemContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void FromChain_NoIgnoreNode_IgnoresNothing()
    {
        var ignored = IgnoredQueryFilters.FromChain(Tail(SqlQuery.AsNoTracking(_db.Items)));

        Assert.Same(IgnoredQueryFilters.None, ignored);
        Assert.False(ignored.IsIgnored(null));
        Assert.False(ignored.IsIgnored("Active"));
    }

    [Fact]
    public void FromChain_IgnoreAll_IgnoresUnnamedAndNamedFilters()
    {
        var ignored = IgnoredQueryFilters.FromChain(Tail(SqlQuery.IgnoreQueryFilters(_db.Items)));

        Assert.True(ignored.All);
        Assert.True(ignored.IsIgnored(null));
        Assert.True(ignored.IsIgnored("Active"));
    }

    [Fact]
    public void FromChain_NamedKeys_IgnoresOnlyThoseKeys()
    {
        var ignored = IgnoredQueryFilters.FromChain(Tail(SqlQuery.IgnoreQueryFilters(_db.Items, ["Active"])));

        Assert.False(ignored.All);
        Assert.True(ignored.IsIgnored("Active"));
        Assert.False(ignored.IsIgnored("Scored"));
        Assert.False(ignored.IsIgnored(null));
    }

    [Fact]
    public void FromChain_SeveralNodesAnywhereInChain_AreCombined()
    {
        var query = SqlQuery.IgnoreQueryFilters(SqlQuery.Take(SqlQuery.IgnoreQueryFilters(_db.Items, ["Active"]), 5), ["Scored"]);

        var ignored = IgnoredQueryFilters.FromChain(Tail(query));

        Assert.True(ignored.IsIgnored("Active"));
        Assert.True(ignored.IsIgnored("Scored"));
        Assert.False(ignored.IsIgnored("Other"));
    }

    [Fact]
    public void FromChain_IgnoreAllWithNamedKeys_IgnoresEverything()
    {
        var query = SqlQuery.IgnoreQueryFilters(SqlQuery.IgnoreQueryFilters(_db.Items, ["Active"]));

        var ignored = IgnoredQueryFilters.FromChain(Tail(query));

        Assert.True(ignored.All);
        Assert.True(ignored.IsIgnored("Other"));
    }

    [Fact]
    public void FromChain_NullTail_Throws()
        => Assert.Throws<ArgumentNullException>("tail", () => IgnoredQueryFilters.FromChain(null!));

    private static QueryNode Tail(IQueryable<Item> query) => ((SqlQueryable<Item>)query).Tail;
}
