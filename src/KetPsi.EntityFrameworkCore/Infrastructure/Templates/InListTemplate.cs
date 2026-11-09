using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>list.Contains(x.Property)</c>, where the list is a captured collection.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InListTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public InListTemplate(string propertyName, int valueIndex)
        : base(valueIndex + 1)
    {
        ValidatePropertyName(propertyName);
        ValidateValueIndex(valueIndex);

        PropertyName = propertyName;
        ValueIndex = valueIndex;
    }

    /// <summary>The property looked up in the list, or a path to one through reference navigations.</summary>
    public string PropertyName { get; }

    /// <summary>The slot holding the list.</summary>
    public int ValueIndex { get; }
}
