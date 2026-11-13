using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// An ordering on a property, or on a path to one through reference navigations such as <c>Blog.Name</c>, or on a
/// computed value: <c>OrderBy</c>, <c>ThenBy</c> or their descending forms.
/// </summary>
internal sealed class OrderNode : QueryNode
{
    public OrderNode(QueryNode parent, string propertyName, bool descending, bool isThenBy)
        : base(parent)
    {
        PropertyName = propertyName;
        Descending = descending;
        IsThenBy = isThenBy;
    }

    public OrderNode(QueryNode parent, ValueTemplate key, IReadOnlyList<object?> values, bool descending, bool isThenBy)
        : base(parent)
    {
        Key = key;
        Values = values;
        Descending = descending;
        IsThenBy = isThenBy;
    }

    /// <summary>The ordered property or path; <see langword="null"/> when the ordering is on <see cref="Key"/>.</summary>
    public string? PropertyName { get; }

    /// <summary>The computed value ordered by; <see langword="null"/> when the ordering is on <see cref="PropertyName"/>.</summary>
    public ValueTemplate? Key { get; }

    /// <summary>The value slots of <see cref="Key"/>.</summary>
    public IReadOnlyList<object?> Values { get; } = [];

    public bool Descending { get; }

    /// <summary><see langword="true"/> for <c>ThenBy</c>, which extends the current ordering instead of replacing it.</summary>
    public bool IsThenBy { get; }
}
