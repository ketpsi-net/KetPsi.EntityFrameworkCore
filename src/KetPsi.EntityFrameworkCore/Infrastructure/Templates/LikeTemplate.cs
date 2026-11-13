using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>EF.Functions.Like(value, pattern)</c> or <c>EF.Functions.Like(value, pattern, escape)</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LikeTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public LikeTemplate(ValueTemplate value, ValueTemplate pattern, ValueTemplate? escape = null)
        : base(Math.Max(Math.Max(value?.ValueCount ?? 0, pattern?.ValueCount ?? 0), escape?.ValueCount ?? 0))
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pattern);

        Value = value;
        Pattern = pattern;
        Escape = escape;
    }

    /// <summary>The string matched.</summary>
    public ValueTemplate Value { get; }

    /// <summary>The pattern, with SQL's wildcards.</summary>
    public ValueTemplate Pattern { get; }

    /// <summary>The escape character, or <see langword="null"/> for none.</summary>
    public ValueTemplate? Escape { get; }
}
