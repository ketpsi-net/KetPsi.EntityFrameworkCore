using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// <c>x.Name.Contains(part)</c>, <c>StartsWith</c> or <c>EndsWith</c>, as EF Core writes them: a <c>LIKE</c> pattern
/// with the wildcards of a value escaped, or a search for a column's value. Matching follows the column's collation.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class StringMatchTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public StringMatchTemplate(StringMatch match, ValueTemplate value, ValueTemplate part)
        : base(Math.Max(value?.ValueCount ?? 0, part?.ValueCount ?? 0))
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(part);
        if (!Enum.IsDefined(match))
        {
            throw new ArgumentOutOfRangeException(nameof(match), match, message: null);
        }

        Match = match;
        Value = value;
        Part = part;
    }

    /// <summary>The test.</summary>
    public StringMatch Match { get; }

    /// <summary>The string tested.</summary>
    public ValueTemplate Value { get; }

    /// <summary>The string or character looked for.</summary>
    public ValueTemplate Part { get; }
}
