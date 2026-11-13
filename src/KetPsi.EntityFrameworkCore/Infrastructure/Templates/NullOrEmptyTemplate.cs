using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>string.IsNullOrEmpty(x.Name)</c>, or <c>string.IsNullOrWhiteSpace(x.Name)</c>, as EF Core writes them.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NullOrEmptyTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public NullOrEmptyTemplate(ValueTemplate value, bool orWhiteSpace)
        : base(value?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
        OrWhiteSpace = orWhiteSpace;
    }

    /// <summary>The string tested.</summary>
    public ValueTemplate Value { get; }

    /// <summary>Whether a string of spaces counts as empty: <c>IsNullOrWhiteSpace</c>.</summary>
    public bool OrWhiteSpace { get; }
}
