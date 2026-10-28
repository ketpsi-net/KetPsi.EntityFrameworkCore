using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Sets one navigation of an entity without reflection, for queries that include related entities.
/// KetPsi.EntityFrameworkCore.Generator emits one implementation per navigation of an included entity type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface INavigationAccessor
{
    /// <summary>The entity type that declares the navigation.</summary>
    Type EntityType { get; }

    /// <summary>The navigation name.</summary>
    string NavigationName { get; }

    /// <summary>Sets a reference navigation.</summary>
    void SetReference(object entity, object? value);

    /// <summary>
    /// Creates the collection of a collection navigation when it is <see langword="null"/>, choosing the collection
    /// type as EF Core does: a concrete property type is constructed, and an interface gets a <c>HashSet&lt;T&gt;</c>
    /// when it can hold one, otherwise a <c>List&lt;T&gt;</c>.
    /// </summary>
    void EnsureCollection(object entity);

    /// <summary>Adds <paramref name="item"/> to a collection navigation, creating the collection first if needed.</summary>
    void AddToCollection(object entity, object item);
}
