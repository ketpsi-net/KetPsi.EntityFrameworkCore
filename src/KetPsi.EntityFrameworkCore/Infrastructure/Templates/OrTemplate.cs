using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>left || right</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OrTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public OrTemplate(PredicateTemplate left, PredicateTemplate right)
        : base(Math.Max(left?.ValueCount ?? 0, right?.ValueCount ?? 0))
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    /// <summary>The left operand.</summary>
    public PredicateTemplate Left { get; }

    /// <summary>The right operand.</summary>
    public PredicateTemplate Right { get; }
}
