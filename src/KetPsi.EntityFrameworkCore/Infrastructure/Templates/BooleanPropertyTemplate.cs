using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>A non-nullable <see cref="bool"/> property used as a predicate: <c>x => x.IsActive</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BooleanPropertyTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public BooleanPropertyTemplate(string propertyName)
        : base(valueCount: 0)
    {
        ValidatePropertyName(propertyName);

        PropertyName = propertyName;
    }

    /// <summary>The boolean entity property.</summary>
    public string PropertyName { get; }
}
