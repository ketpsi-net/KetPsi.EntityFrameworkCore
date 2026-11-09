namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// An ordering on a property, or on a path to one through reference navigations such as <c>Blog.Name</c>: <c>OrderBy</c>,
/// <c>ThenBy</c> or their descending forms.
/// </summary>
internal sealed class OrderNode(QueryNode parent, string propertyName, bool descending, bool isThenBy) : QueryNode(parent)
{
    public string PropertyName { get; } = propertyName;

    public bool Descending { get; } = descending;

    /// <summary><see langword="true"/> for <c>ThenBy</c>, which extends the current ordering instead of replacing it.</summary>
    public bool IsThenBy { get; } = isThenBy;
}
