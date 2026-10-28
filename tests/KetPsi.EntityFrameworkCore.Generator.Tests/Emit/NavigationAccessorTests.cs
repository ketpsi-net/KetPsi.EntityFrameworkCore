using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class NavigationAccessorTests
{
    private const string Source = TestSources.Usings + """
        using System.Collections.ObjectModel;

        namespace Nav;

        public sealed class Owner
        {
            public int Id { get; set; }
            public ICollection<Item>? Items { get; set; }
            public IList<Item>? Listed { get; set; }
            public IEnumerable<Item>? Enumerated { get; set; }
            public IReadOnlyCollection<Item>? ReadOnly { get; set; }
            public Collection<Item>? Concrete { get; set; }
            public List<Item> Ready { get; } = [];
            public List<Item>? Missing { get; }
            public Item? Favorite { get; private set; }
        }

        public sealed class Item
        {
            public int Id { get; set; }
            public int OwnerId { get; set; }
            public Owner Owner { get; set; } = null!;
        }

        public sealed class Unrelated
        {
            public int Id { get; set; }
            public List<Item> Items { get; set; } = [];
        }

        public sealed class NavDb : DbContext
        {
            public DbSet<Owner> Owners => Set<Owner>();
            public DbSet<Unrelated> Unrelated => Set<Unrelated>();
        }

        public static class Queries
        {
            public static object Owners(NavDb db) => db.Owners.Include(o => o.Items).ToList();

            public static object Unrelated(NavDb db) => db.Unrelated.AsNoTracking().ToList();
        }
        """;

    private static readonly Lazy<(GeneratorRun Run, Assembly Assembly)> Compiled = new(static () =>
    {
        var run = GeneratorHost.Run(GeneratorHost.Compile("NavigationAccessors", Source));
        var assembly = run.Load();
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return (run, assembly);
    });

    private static Type OwnerType => Compiled.Value.Assembly.GetType("Nav.Owner")!;

    private static Type ItemType => Compiled.Value.Assembly.GetType("Nav.Item")!;

    [Fact]
    public void Accessors_MatchSnapshot()
        => Snapshot.Match(Compiled.Value.Run.Generated(ReaderEmitter.HintName), "NavigationAccessors");

    [Fact]
    public void Accessors_CompileWithoutDiagnosticsOrWarnings()
    {
        Assert.Empty(Compiled.Value.Run.Diagnostics);
        Assert.Empty(Compiled.Value.Run.OutputProblems());
    }

    [Fact]
    public void Accessors_OnlyForEntitiesOnEitherSideOfAnInclude()
    {
        var generated = Compiled.Value.Run.Generated(ReaderEmitter.HintName);

        Assert.Contains("Nav_Owner_Items_Navigation", generated, StringComparison.Ordinal);
        Assert.Contains("Nav_Item_Owner_Navigation", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Nav_Unrelated_Items_Navigation", generated, StringComparison.Ordinal);
        Assert.Contains("Nav_Unrelated_Reader", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Items", typeof(HashSet<>))]
    [InlineData("Listed", typeof(List<>))]
    [InlineData("Enumerated", typeof(HashSet<>))]
    [InlineData("ReadOnly", typeof(HashSet<>))]
    [InlineData("Concrete", typeof(Collection<>))]
    public void EnsureCollection_CreatesTheCollectionEfCoreWould(string navigation, Type expected)
    {
        var owner = Activator.CreateInstance(OwnerType)!;

        Accessor(OwnerType, navigation).EnsureCollection(owner);

        Assert.Equal(expected.MakeGenericType(ItemType), OwnerType.GetProperty(navigation)!.GetValue(owner)!.GetType());
    }

    [Fact]
    public void EnsureCollection_KeepsAnExistingCollection()
    {
        var owner = Activator.CreateInstance(OwnerType)!;
        var ready = OwnerType.GetProperty("Ready")!.GetValue(owner);

        Accessor(OwnerType, "Ready").EnsureCollection(owner);

        Assert.Same(ready, OwnerType.GetProperty("Ready")!.GetValue(owner));
    }

    [Fact]
    public void AddToCollection_CreatesAndAdds()
    {
        var owner = Activator.CreateInstance(OwnerType)!;
        var item = Activator.CreateInstance(ItemType)!;

        Accessor(OwnerType, "Items").AddToCollection(owner, item);
        Accessor(OwnerType, "Ready").AddToCollection(owner, item);

        Assert.Same(item, ((IEnumerable)OwnerType.GetProperty("Items")!.GetValue(owner)!).Cast<object>().Single());
        Assert.Same(item, ((IEnumerable)OwnerType.GetProperty("Ready")!.GetValue(owner)!).Cast<object>().Single());
    }

    [Fact]
    public void EnsureCollection_NullGetterOnlyCollection_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Accessor(OwnerType, "Missing").EnsureCollection(Activator.CreateInstance(OwnerType)!));

        Assert.Contains("Nav.Owner.Missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetReference_PrivateSetter_UsesUnsafeAccessor()
    {
        var owner = Activator.CreateInstance(OwnerType)!;
        var item = Activator.CreateInstance(ItemType)!;

        Accessor(OwnerType, "Favorite").SetReference(owner, item);

        Assert.Same(item, OwnerType.GetProperty("Favorite")!.GetValue(owner));
    }

    [Fact]
    public void SetReference_OnInverseNavigation_Works()
    {
        var owner = Activator.CreateInstance(OwnerType)!;
        var item = Activator.CreateInstance(ItemType)!;

        Accessor(ItemType, "Owner").SetReference(item, owner);

        Assert.Same(owner, ItemType.GetProperty("Owner")!.GetValue(item));
    }

    [Fact]
    public void WrongKindOfNavigation_Throws()
    {
        var owner = Activator.CreateInstance(OwnerType)!;

        Assert.Throws<NotSupportedException>(() => Accessor(OwnerType, "Items").SetReference(owner, null));
        Assert.Throws<NotSupportedException>(() => Accessor(OwnerType, "Favorite").EnsureCollection(owner));
        Assert.Throws<NotSupportedException>(() => Accessor(OwnerType, "Favorite").AddToCollection(owner, new object()));
    }

    private static INavigationAccessor Accessor(Type entityType, string navigation)
    {
        _ = Compiled.Value;
        var accessor = EntityNavigations.Get(entityType, navigation);
        Assert.Equal(entityType, accessor.EntityType);
        Assert.Equal(navigation, accessor.NavigationName);
        return accessor;
    }
}
