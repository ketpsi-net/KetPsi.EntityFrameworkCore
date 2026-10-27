using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Binding;

public sealed class BoundEntityOrdinalsTests
{
    private readonly BoundEntity _entity = Bind();

    [Fact]
    public void GetOrdinals_MapsNamesToSelectedColumnPositions()
        => Assert.Equal(
            [3, 0, 2, 1],
            _entity.GetOrdinals([nameof(Item.Score), nameof(Item.Id), nameof(Item.Name), nameof(Item.IsActive)]));

    [Fact]
    public void GetOrdinals_UnmappedName_IsMinusOne()
        => Assert.Equal([0, -1], _entity.GetOrdinals([nameof(Item.Id), "Unmapped"]));

    [Fact]
    public void GetOrdinals_SameListInstance_ReturnsCachedArray()
    {
        string[] names = [nameof(Item.Id)];

        Assert.Same(_entity.GetOrdinals(names), _entity.GetOrdinals(names));
    }

    [Fact]
    public void GetOrdinals_EqualButDistinctLists_MapEach()
    {
        var first = _entity.GetOrdinals([nameof(Item.Id)]);
        var second = _entity.GetOrdinals([nameof(Item.Id)]);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrdinals_Null_Throws()
        => Assert.Throws<ArgumentNullException>("propertyNames", () => _entity.GetOrdinals(null!));

    private static BoundEntity Bind()
    {
        using var db = new ItemContext();
        return ModelBinder.GetEntity(db.Model, typeof(Item));
    }
}
