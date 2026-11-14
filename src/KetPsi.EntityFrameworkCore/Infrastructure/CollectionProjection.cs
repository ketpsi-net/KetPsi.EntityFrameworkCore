using System.Collections;
using System.ComponentModel;
using System.Data.Common;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// A collection navigation a <c>Select</c> projects, such as <c>x.Posts</c> or
/// <c>x.Posts.Where(p =&gt; p.IsPublic).OrderBy(p =&gt; p.Title).Select(p =&gt; p.Title).ToList()</c>. It is loaded with
/// one more statement after the projected rows are read, which matches the keys of their parents, as an included
/// collection is; the related entity's query filters apply.
/// </summary>
/// <remarks>
/// The names in <see cref="Predicate"/>, <see cref="Orderings"/>, <see cref="PropertyNames"/> and
/// <see cref="EntityPaths"/> are property paths of the related entity, such as <c>Title</c> or <c>Author.Name</c>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class CollectionProjection
{
    private protected CollectionProjection(
        string navigationPath,
        PredicateTemplate? predicate,
        object?[] slotValues,
        IReadOnlyList<CollectionOrdering> orderings,
        IReadOnlyList<string> propertyNames,
        IReadOnlyList<string> entityPaths)
    {
        ArgumentException.ThrowIfNullOrEmpty(navigationPath);
        ArgumentNullException.ThrowIfNull(slotValues);
        ArgumentNullException.ThrowIfNull(orderings);
        ArgumentNullException.ThrowIfNull(propertyNames);
        ArgumentNullException.ThrowIfNull(entityPaths);
        if (propertyNames.Count == 0 && entityPaths.Count == 0)
        {
            throw new ArgumentException(ExceptionMessages.ProjectionRequiresColumns, nameof(propertyNames));
        }

        NavigationPath = navigationPath;
        Predicate = predicate;
        SlotValues = slotValues;
        Orderings = orderings;
        PropertyNames = propertyNames;
        EntityPaths = entityPaths;
    }

    /// <summary>The collection navigation, or a path to one through references, such as <c>Blog.Posts</c>.</summary>
    public string NavigationPath { get; }

    /// <summary>The condition on the related rows, or <see langword="null"/> for all of them.</summary>
    public PredicateTemplate? Predicate { get; }

    /// <summary>The value slots of <see cref="Predicate"/>.</summary>
    public object?[] SlotValues { get; }

    /// <summary>
    /// The orderings of the elements, first to last. The related entity's key orders them last, so elements without an
    /// ordering come in key order.
    /// </summary>
    public IReadOnlyList<CollectionOrdering> Orderings { get; }

    /// <summary>The property paths the element reader reads, in the order of its ordinals.</summary>
    public IReadOnlyList<string> PropertyNames { get; }

    /// <summary>The navigation paths of the entities the element reader uses: <c>""</c> for the related entity itself.</summary>
    public IReadOnlyList<string> EntityPaths { get; }

    /// <summary>Creates the list the elements of one projected row are added to: a <c>List&lt;TElement&gt;</c>.</summary>
    internal abstract IList CreateItems();

    /// <summary>Reads one element from the current row of the collection's statement.</summary>
    internal abstract object? ReadElement(DbDataReader row, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities);
}

/// <summary>A projected collection whose elements are <typeparamref name="TElement"/>.</summary>
/// <typeparam name="TElement">The element type: the related entity, or what the collection's <c>Select</c> builds.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CollectionProjection<TElement> : CollectionProjection
{
    private readonly ProjectionReader<TElement> _reader;

    /// <summary>Creates the projected collection.</summary>
    /// <param name="navigationPath">The collection navigation, or a path to one through references, such as <c>Blog.Posts</c>.</param>
    /// <param name="predicate">The condition on the related rows, or <see langword="null"/> for all of them.</param>
    /// <param name="slotValues">The value slots of <paramref name="predicate"/>.</param>
    /// <param name="orderings">The orderings of the elements, first to last.</param>
    /// <param name="propertyNames">The property paths the element reader reads, in the order of its ordinals.</param>
    /// <param name="entityPaths">The navigation paths of the entities the element reader uses.</param>
    /// <param name="reader">The generated reader that builds one element from a row of the related entity.</param>
    /// <exception cref="ArgumentException">The element reads no property and no entity.</exception>
    public CollectionProjection(
        string navigationPath,
        PredicateTemplate? predicate,
        object?[] slotValues,
        IReadOnlyList<CollectionOrdering> orderings,
        IReadOnlyList<string> propertyNames,
        IReadOnlyList<string> entityPaths,
        ProjectionReader<TElement> reader)
        : base(navigationPath, predicate, slotValues, orderings, propertyNames, entityPaths)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    internal override IList CreateItems() => new List<TElement>();

    internal override object? ReadElement(DbDataReader row, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities)
        => _reader(row, ordinals, entities);
}

/// <summary>An ordering of the elements of a projected collection, on a property path of the related entity.</summary>
/// <param name="PropertyPath">The ordered property, or a path to one through references, such as <c>Author.Name</c>.</param>
/// <param name="Descending">Whether the ordering is descending.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct CollectionOrdering(string PropertyPath, bool Descending);
