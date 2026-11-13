using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>-x.Quantity</c> or <c>~x.Flags</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class UnaryValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public UnaryValueTemplate(UnaryValueOperator op, ValueTemplate operand)
        : base(MaxValueCount(operand))
    {
        ArgumentNullException.ThrowIfNull(operand);
        if (!Enum.IsDefined(op))
        {
            throw new ArgumentOutOfRangeException(nameof(op), op, message: null);
        }

        Operator = op;
        Operand = operand;
    }

    /// <summary>The operator.</summary>
    public UnaryValueOperator Operator { get; }

    /// <summary>The operand.</summary>
    public ValueTemplate Operand { get; }
}
