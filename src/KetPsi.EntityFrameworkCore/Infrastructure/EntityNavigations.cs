using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The generated navigation accessors. Code emitted by KetPsi.EntityFrameworkCore.Generator registers them;
/// applications don't call this.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityNavigations
{
    private static readonly ConcurrentDictionary<(Type EntityType, string Name), INavigationAccessor> Accessors = new();

    /// <summary>
    /// Registers an accessor unless one is already registered for the same navigation. Accessors generated in
    /// different assemblies for the same navigation are equivalent, so the first registration is kept.
    /// </summary>
    public static void Register(INavigationAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        Accessors.TryAdd((accessor.EntityType, accessor.NavigationName), accessor);
    }

    /// <exception cref="InvalidOperationException">No accessor is registered for the navigation.</exception>
    internal static INavigationAccessor Get(Type entityType, string navigationName)
        => Accessors.TryGetValue((entityType, navigationName), out var accessor)
            ? accessor
            : throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.NavigationAccessorNotRegistered, entityType, navigationName));
}
