using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>An entity type bound to its table, with the predicates bound against it so far.</summary>
internal sealed class BoundEntity
{
    private readonly Dictionary<string, BoundProperty> _propertiesByName;
    private readonly Dictionary<string, BoundNavigation> _navigationsByName;
    private readonly Dictionary<string, BoundSkipNavigation> _skipNavigationsByName;
    private readonly ConcurrentDictionary<PredicateTemplate, BoundPredicate> _predicates = new();
    private readonly ConcurrentDictionary<ValueTemplate, BoundValue> _values = new();
    private readonly ConcurrentDictionary<IReadOnlyList<string>, int[]> _ordinals = new(ReferenceEqualityComparer.Instance);

    public BoundEntity(
        Type clrType,
        string table,
        string? schema,
        IReadOnlyList<BoundProperty> properties,
        IReadOnlyList<BoundProperty>? primaryKey,
        IReadOnlyList<string?> filterKeys,
        IReadOnlyList<BoundNavigation>? navigations = null,
        IReadOnlyList<BoundSkipNavigation>? skipNavigations = null,
        IReadOnlyList<BoundForeignKey>? foreignKeys = null)
    {
        ClrType = clrType;
        Table = table;
        Schema = schema;
        Properties = properties;
        PrimaryKey = primaryKey;
        ShadowProperties = [.. properties.Where(static p => p.IsShadow)];
        FilterKeys = filterKeys;
        Navigations = navigations ?? [];
        ForeignKeys = foreignKeys ?? [];
        _propertiesByName = properties.ToDictionary(static p => p.Name, StringComparer.Ordinal);
        _navigationsByName = Navigations.ToDictionary(static n => n.Name, StringComparer.Ordinal);
        _skipNavigationsByName = (skipNavigations ?? []).ToDictionary(static n => n.Name, StringComparer.Ordinal);
    }

    public Type ClrType { get; }

    public string Table { get; }

    public string? Schema { get; }

    /// <summary>Every mapped property, in model order.</summary>
    public IReadOnlyList<BoundProperty> Properties { get; }

    /// <summary>
    /// The primary key properties in key order, or <see langword="null"/> for a keyless entity type, which EF Core
    /// never tracks.
    /// </summary>
    public IReadOnlyList<BoundProperty>? PrimaryKey { get; }

    /// <summary>The shadow properties, in model order.</summary>
    public IReadOnlyList<BoundProperty> ShadowProperties { get; }

    /// <summary>The keys of the query filters the model declares for the entity; <see langword="null"/> for an unnamed filter.</summary>
    public IReadOnlyList<string?> FilterKeys { get; }

    /// <summary>The navigations to other entity types, in model order. Many-to-many navigations are not included.</summary>
    public IReadOnlyList<BoundNavigation> Navigations { get; }

    /// <summary>
    /// The foreign keys to other entities' primary keys that have a navigation on either side, in model order.
    /// </summary>
    public IReadOnlyList<BoundForeignKey> ForeignKeys { get; }

    /// <exception cref="InvalidOperationException">The entity has no navigation named <paramref name="navigationName"/>.</exception>
    /// <exception cref="NotSupportedException">The navigation is many-to-many.</exception>
    public BoundNavigation GetNavigation(string navigationName)
    {
        if (_navigationsByName.TryGetValue(navigationName, out var navigation))
        {
            return navigation;
        }

        throw _skipNavigationsByName.ContainsKey(navigationName)
            ? new NotSupportedException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ManyToManyNotSupported, ClrType, navigationName))
            : new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NavigationNotFound, ClrType, navigationName));
    }

    /// <summary>The many-to-many navigation named <paramref name="navigationName"/>, or <see langword="null"/>.</summary>
    public BoundSkipNavigation? FindSkipNavigation(string navigationName)
        => _skipNavigationsByName.TryGetValue(navigationName, out var navigation) ? navigation : null;

    /// <summary>Whether the entity maps a property named <paramref name="propertyName"/>.</summary>
    public bool HasProperty(string propertyName) => _propertiesByName.ContainsKey(propertyName);

    /// <exception cref="InvalidOperationException">The entity has no mapped property named <paramref name="propertyName"/>.</exception>
    public BoundProperty GetProperty(string propertyName)
        => _propertiesByName.TryGetValue(propertyName, out var property)
            ? property
            : throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.PropertyNotMapped, ClrType, propertyName));

    /// <summary>
    /// Maps each property name to the ordinal of its column in a statement that selects <see cref="Properties"/>
    /// in order, or -1 when the model does not map the property. Cached per name list instance.
    /// </summary>
    public int[] GetOrdinals(IReadOnlyList<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        return _ordinals.GetOrAdd(propertyNames, static (names, entity) => entity.MapOrdinals(names), this);
    }

    private int[] MapOrdinals(IReadOnlyList<string> propertyNames)
    {
        var ordinals = new int[propertyNames.Count];
        for (var i = 0; i < ordinals.Length; i++)
        {
            ordinals[i] = _propertiesByName.TryGetValue(propertyNames[i], out var property) ? IndexOf(property) : -1;
        }

        return ordinals;
    }

    private int IndexOf(BoundProperty property)
    {
        for (var i = 0; i < Properties.Count; i++)
        {
            if (ReferenceEquals(Properties[i], property))
            {
                return i;
            }
        }

        throw new UnreachableException(property.Name);
    }

    /// <summary>Binds <paramref name="template"/> against this entity, once per template instance.</summary>
    public BoundPredicate BindPredicate(PredicateTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return _predicates.GetOrAdd(template, static (t, entity) => PredicateBinder.Bind(t, entity), this);
    }

    /// <summary>Binds the computed value <paramref name="template"/> against this entity, once per template instance.</summary>
    public BoundValue BindValue(ValueTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return _values.GetOrAdd(template, static (t, entity) => PredicateBinder.Bind(t, entity.GetProperty, entity.ClrType), this);
    }
}
