using System.Collections.Concurrent;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>An entity type bound to its table, with the predicates bound against it so far.</summary>
internal sealed class BoundEntity
{
    private readonly Dictionary<string, BoundProperty> _propertiesByName;
    private readonly ConcurrentDictionary<PredicateTemplate, BoundPredicate> _predicates = new();

    public BoundEntity(Type clrType, string table, string? schema, IReadOnlyList<BoundProperty> properties)
    {
        ClrType = clrType;
        Table = table;
        Schema = schema;
        Properties = properties;
        _propertiesByName = properties.ToDictionary(static p => p.Name, StringComparer.Ordinal);
    }

    public Type ClrType { get; }

    public string Table { get; }

    public string? Schema { get; }

    /// <summary>Every mapped property, in model order.</summary>
    public IReadOnlyList<BoundProperty> Properties { get; }

    /// <exception cref="InvalidOperationException">The entity has no mapped property named <paramref name="propertyName"/>.</exception>
    public BoundProperty GetProperty(string propertyName)
        => _propertiesByName.TryGetValue(propertyName, out var property)
            ? property
            : throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.PropertyNotMapped, ClrType, propertyName));

    /// <summary>Binds <paramref name="template"/> against this entity, once per template instance.</summary>
    public BoundPredicate BindPredicate(PredicateTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return _predicates.GetOrAdd(template, static (t, entity) => PredicateBinder.Bind(t, entity), this);
    }
}
