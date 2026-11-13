using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// <c>left op right</c> on computed values, such as <c>x.Price &gt; x.Cost</c> or <c>x.Price * x.Quantity &gt; limit</c>.
/// Nulls compare as in C#: two nulls are equal, and an ordering comparison with a null is false.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ValueComparisonTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public ValueComparisonTemplate(ValueTemplate left, ComparisonOperator comparison, ValueTemplate right)
        : base(Math.Max(left?.ValueCount ?? 0, right?.ValueCount ?? 0))
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!Enum.IsDefined(comparison))
        {
            throw new ArgumentOutOfRangeException(nameof(comparison), comparison, message: null);
        }

        Left = left;
        Comparison = comparison;
        Right = right;
    }

    /// <summary>The left operand.</summary>
    public ValueTemplate Left { get; }

    /// <summary>The comparison.</summary>
    public ComparisonOperator Comparison { get; }

    /// <summary>The right operand.</summary>
    public ValueTemplate Right { get; }
}
