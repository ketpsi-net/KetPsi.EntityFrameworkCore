using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A comparison with the <see langword="null"/> literal, or <c>HasValue</c>:
/// <c>x.Property == null</c>, <c>x.Property != null</c>, <c>x.Property.HasValue</c>, or <c>x.Navigation == null</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NullCheckTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="propertyName">The checked property or reference navigation, or a path to one, such as <c>Blog.Owner</c>.</param>
    /// <param name="isNull"><see langword="true"/> to match nulls; <see langword="false"/> to match non-nulls.</param>
    public NullCheckTemplate(string propertyName, bool isNull)
        : base(valueCount: 0)
    {
        ValidatePropertyName(propertyName);

        PropertyName = propertyName;
        IsNull = isNull;
    }

    /// <summary>The checked property or reference navigation, or a path to one, such as <c>Blog.Owner</c>.</summary>
    public string PropertyName { get; }

    /// <summary><see langword="true"/> to match nulls; <see langword="false"/> to match non-nulls.</summary>
    public bool IsNull { get; }
}
