using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A computed <see cref="bool"/> used as a predicate, such as <c>x =&gt; x.IsFeatured ?? false</c> or
/// <c>x =&gt; x.IsActive ^ x.IsFeatured</c>: true when the value is true.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BooleanValueTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public BooleanValueTemplate(ValueTemplate value)
        : base(value?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
    }

    /// <summary>The boolean value.</summary>
    public ValueTemplate Value { get; }
}
