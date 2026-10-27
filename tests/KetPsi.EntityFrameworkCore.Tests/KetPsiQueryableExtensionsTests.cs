using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests;

// Without the generator, WhereIf is plain LINQ; these tests run it on LINQ to Objects.
public sealed class KetPsiQueryableExtensionsTests
{
    private static readonly IQueryable<Item> Items = new List<Item>
    {
        new() { Id = 1, IsActive = true },
        new() { Id = 2, IsActive = false },
    }.AsQueryable();

    [Fact]
    public void WhereIf_True_FiltersWithPredicate()
        => Assert.Equal([1], Items.WhereIf(true, i => i.IsActive).Select(i => i.Id));

    [Fact]
    public void WhereIf_False_ReturnsSourceUnchanged()
        => Assert.Same(Items, Items.WhereIf(false, i => i.IsActive));

    [Fact]
    public void WhereIf_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("source", () => KetPsiQueryableExtensions.WhereIf<Item>(null!, true, i => i.IsActive));
        Assert.Throws<ArgumentNullException>("predicate", () => Items.WhereIf(true, null!));
    }
}
