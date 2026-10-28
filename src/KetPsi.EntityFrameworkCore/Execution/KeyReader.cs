using System.Collections;
using System.Data.Common;
using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Reads an entity's primary key, or another list of its properties such as a foreign key, from a result row, as the
/// CLR values EF Core compares keys by.
/// </summary>
internal sealed class KeyReader
{
    private readonly (int Ordinal, Type ClrType)[] _key;

    /// <param name="entity">The bound entity; it must have a primary key.</param>
    /// <param name="offset">The ordinal of the entity's first column in the row.</param>
    public KeyReader(BoundEntity entity, int offset = 0)
        : this(entity, entity.PrimaryKey ?? throw new ArgumentNullException(nameof(entity), ExceptionMessages.KeyReaderRequiresKey), offset)
    {
    }

    /// <param name="entity">The bound entity.</param>
    /// <param name="properties">The properties to read, in order; properties of <paramref name="entity"/>.</param>
    /// <param name="offset">The ordinal of the entity's first column in the row.</param>
    public KeyReader(BoundEntity entity, IReadOnlyList<BoundProperty> properties, int offset)
    {
        _key = [.. properties.Select(p => (offset + Ordinal(entity, p), p.ClrType))];
    }

    /// <summary>The ordinal of the first key column, which is null when a left join found no row.</summary>
    public int FirstOrdinal => _key[0].Ordinal;

    /// <summary>Compares keys value by value, and binary keys byte by byte.</summary>
    public static IEqualityComparer<object?[]> Comparer { get; } = new KeyComparer();

    /// <summary>Returns the key values of the current row, in key order.</summary>
    public object?[] Read(DbDataReader row)
    {
        var values = new object?[_key.Length];
        for (var i = 0; i < _key.Length; i++)
        {
            values[i] = ToClrValue(row.GetValue(_key[i].Ordinal), _key[i].ClrType);
        }

        return values;
    }

    /// <summary>Converts a provider value to the property's CLR value.</summary>
    internal static object? ToClrValue(object value, Type clrType)
    {
        if (value is DBNull)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return type.IsEnum ? Enum.ToObject(type, value) : value;
    }

    /// <summary>The statement selects every bound property in model order, so a property's ordinal is its index.</summary>
    internal static int Ordinal(BoundEntity entity, BoundProperty property)
    {
        for (var i = 0; i < entity.Properties.Count; i++)
        {
            if (ReferenceEquals(entity.Properties[i], property))
            {
                return i;
            }
        }

        throw new ArgumentException(property.Name, nameof(property));
    }

    private sealed class KeyComparer : IEqualityComparer<object?[]>
    {
        public bool Equals(object?[]? x, object?[]? y) => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

        public int GetHashCode(object?[] obj) => StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
    }
}
