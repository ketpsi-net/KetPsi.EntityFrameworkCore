using System.Data.Common;
using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure;

// Readers are stored per entity type for the whole process, so each test uses its own entity type.
public sealed class EntityReadersTests
{
    [Fact]
    public void Register_ThenGet_ReturnsReader()
    {
        var reader = new Reader<EntityA>();

        EntityReaders.Register(reader);

        Assert.Same(reader, EntityReaders.Get<EntityA>());
    }

    [Fact]
    public void Register_SameReaderTypeAgain_KeepsFirst()
    {
        var first = new Reader<EntityB>();

        EntityReaders.Register(first);
        EntityReaders.Register(new Reader<EntityB>());

        Assert.Same(first, EntityReaders.Get<EntityB>());
    }

    // Each assembly that queries an entity generates its own reader type for it.
    [Fact]
    public void Register_ReaderFromAnotherAssembly_KeepsFirst()
    {
        var first = new Reader<EntityC>();

        EntityReaders.Register(first);
        EntityReaders.Register(new OtherReader<EntityC>());

        Assert.Same(first, EntityReaders.Get<EntityC>());
    }

    [Fact]
    public void Get_Unregistered_ThrowsNamingEntity()
    {
        var exception = Assert.Throws<InvalidOperationException>(EntityReaders.Get<EntityD>);

        Assert.Contains(typeof(EntityD).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_Null_Throws()
        => Assert.Throws<ArgumentNullException>("reader", () => EntityReaders.Register<EntityA>(null!));

    private sealed class EntityA;

    private sealed class EntityB;

    private sealed class EntityC;

    private sealed class EntityD;

    private class Reader<T> : IEntityReader<T>
    {
        public IReadOnlyList<string> PropertyNames { get; } = [];

        public T Read(DbDataReader reader, ReadOnlySpan<int> ordinals) => throw new NotSupportedException();
    }

    private sealed class OtherReader<T> : Reader<T>;
}
