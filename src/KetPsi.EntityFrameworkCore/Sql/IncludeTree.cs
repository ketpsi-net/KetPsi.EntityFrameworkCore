using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>An entity and the navigations included from it, each leading to the included entity's own tree.</summary>
internal sealed class IncludeTree(BoundEntity entity)
{
    private readonly List<IncludeBranch> _branches = [];

    public BoundEntity Entity { get; } = entity;

    /// <summary>The included navigations, in the order they were first included.</summary>
    public IReadOnlyList<IncludeBranch> Branches => _branches;

    public bool HasCollections => _branches.Exists(static b => b.Navigation.IsCollection);

    /// <summary>
    /// Merges the include paths of a chain into one tree, as EF Core does: repeating a path, or a prefix of one,
    /// includes it once.
    /// </summary>
    /// <param name="root">The queried entity.</param>
    /// <param name="tail">The last node of the chain.</param>
    /// <param name="bindEntity">Binds the target entity of a navigation.</param>
    /// <returns>The tree, or <see langword="null"/> when the chain includes nothing.</returns>
    /// <exception cref="InvalidOperationException">A path names a navigation the entity does not have.</exception>
    /// <exception cref="NotSupportedException">A path names a many-to-many navigation.</exception>
    public static IncludeTree? FromChain(BoundEntity root, QueryNode tail, Func<Type, BoundEntity> bindEntity)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(bindEntity);

        var paths = new List<IReadOnlyList<string>>();
        for (var node = tail; node is not null; node = node.Parent)
        {
            if (node is IncludeNode include)
            {
                paths.Add(include.Path);
            }
        }

        if (paths.Count == 0)
        {
            return null;
        }

        paths.Reverse();
        var tree = new IncludeTree(root);
        foreach (var path in paths)
        {
            var current = tree;
            foreach (var name in path)
            {
                current = current.GetOrAdd(name, bindEntity);
            }
        }

        return tree;
    }

    private IncludeTree GetOrAdd(string navigationName, Func<Type, BoundEntity> bindEntity)
    {
        var existing = _branches.Find(b => b.Navigation.Name == navigationName);
        if (existing is not null)
        {
            return existing.Target;
        }

        var navigation = Entity.GetNavigation(navigationName);
        var branch = new IncludeBranch(navigation, new IncludeTree(bindEntity(navigation.TargetClrType)));
        _branches.Add(branch);
        return branch.Target;
    }
}

/// <summary>An included navigation and the tree of the entity it leads to.</summary>
internal sealed record IncludeBranch(BoundNavigation Navigation, IncludeTree Target);
