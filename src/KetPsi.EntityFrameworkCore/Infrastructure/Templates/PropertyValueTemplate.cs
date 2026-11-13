using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>An entity property in a computed value: <c>x.Price</c>, or a path through reference navigations such as <c>x.Blog.Name</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PropertyValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public PropertyValueTemplate(string propertyName)
        : base(valueCount: 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        PropertyName = propertyName;
    }

    /// <summary>The property, or a path to one through reference navigations, such as <c>Blog.Name</c>.</summary>
    public string PropertyName { get; }
}
