using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>!operand</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NotTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public NotTemplate(PredicateTemplate operand)
        : base(operand?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(operand);

        Operand = operand;
    }

    /// <summary>The negated predicate.</summary>
    public PredicateTemplate Operand { get; }
}
