using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>An entity type bound to its table, with the predicates bound against it so far.</summary>
internal sealed class BoundEntity
{
    private readonly Dictionary<string, BoundProperty> _propertiesByName;
    private readonly ConcurrentDictionary<PredicateTemplate, BoundPredicate> _predicates = new();
    private readonly ConcurrentDictionary<IReadOnlyList<string>, int[]> _ordinals = new(ReferenceEqualityComparer.Instance);

    public BoundEntity(Type clrType, string table, string? schema, IReadOnlyList<BoundProperty> properties, IReadOnlyList<string?> filterKeys)
    {
        ClrType = clrType;
        Table = table;
        Schema = schema;
        Properties = properties;
        FilterKeys = filterKeys;
        _propertiesByName = properties.ToDictionary(static p => p.Name, StringComparer.Ordinal);
    }

    public Type ClrType { get; }

    public string Table { get; }

    public string? Schema { get; }

    /// <summary>Every mapped property, in model order.</summary>
    public IReadOnlyList<BoundProperty> Properties { get; }

    /// <summary>The keys of the query filters the model declares for the entity; <see langword="null"/> for an unnamed filter.</summary>
    public IReadOnlyList<string?> FilterKeys { get; }

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
}
