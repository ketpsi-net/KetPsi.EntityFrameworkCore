using System.ComponentModel;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// One <c>SetProperty</c> of <c>ExecuteUpdate</c>: the property it sets, and the value it sets it to, computed in SQL
/// from the row's columns and its own value slots.
/// </summary>
/// <remarks>
/// A constant or a variable is a <see cref="SlotValueTemplate"/> of slot 0, sent as a parameter typed and converted
/// like the property's column, as EF Core sends it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class UpdateSetter
{
    /// <summary>Creates the setter.</summary>
    /// <param name="propertyName">The property of the updated entity that is set.</param>
    /// <param name="value">The value it is set to; its names are properties of the updated entity.</param>
    /// <param name="values">The value slots of <paramref name="value"/>.</param>
    /// <exception cref="ArgumentException">The property name is empty, or the slots are not as many as the value reads.</exception>
    public UpdateSetter(string propertyName, ValueTemplate value, object?[] values)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != value.ValueCount)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ValueCountMismatch, value.ValueCount, values.Length), nameof(values));
        }

        PropertyName = propertyName;
        Value = value;
        Values = values;
    }

    /// <summary>The property of the updated entity that is set.</summary>
    public string PropertyName { get; }

    /// <summary>The value it is set to.</summary>
    public ValueTemplate Value { get; }

    /// <summary>The value slots of <see cref="Value"/>.</summary>
    public IReadOnlyList<object?> Values { get; }
}
