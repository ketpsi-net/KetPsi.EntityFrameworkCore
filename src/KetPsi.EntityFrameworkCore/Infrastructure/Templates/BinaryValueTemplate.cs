using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// <c>left op right</c>, such as <c>x.Price * x.Quantity</c> or <c>x.First + " " + x.Last</c>. The operands have the
/// operator's type: C#'s implicit conversions are written as <see cref="ConvertValueTemplate"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BinaryValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public BinaryValueTemplate(ValueOperator op, ValueTemplate left, ValueTemplate right)
        : base(MaxValueCount(left, right))
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!Enum.IsDefined(op))
        {
            throw new ArgumentOutOfRangeException(nameof(op), op, message: null);
        }

        Operator = op;
        Left = left;
        Right = right;
    }

    /// <summary>The operator.</summary>
    public ValueOperator Operator { get; }

    /// <summary>The left operand.</summary>
    public ValueTemplate Left { get; }

    /// <summary>The right operand.</summary>
    public ValueTemplate Right { get; }
}
