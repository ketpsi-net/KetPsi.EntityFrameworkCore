using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// <c>x.Property op value</c>, where the value is a constant or a captured variable. Whether the value is
/// <see langword="null"/> is decided when the query runs.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ComparisonTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public ComparisonTemplate(string propertyName, ComparisonOperator comparison, int valueIndex)
        : base(valueIndex + 1)
    {
        ValidatePropertyName(propertyName);
        ValidateValueIndex(valueIndex);
        if (!Enum.IsDefined(comparison))
        {
            throw new ArgumentOutOfRangeException(nameof(comparison), comparison, message: null);
        }

        PropertyName = propertyName;
        Comparison = comparison;
        ValueIndex = valueIndex;
    }

    /// <summary>The compared entity property.</summary>
    public string PropertyName { get; }

    /// <summary>The comparison, with the property on the left.</summary>
    public ComparisonOperator Comparison { get; }

    /// <summary>The slot holding the value.</summary>
    public int ValueIndex { get; }
}
