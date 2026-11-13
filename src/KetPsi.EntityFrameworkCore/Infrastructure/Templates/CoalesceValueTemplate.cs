using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>left ?? right</c>, or <c>left.GetValueOrDefault(right)</c>: <c>COALESCE(left, right)</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CoalesceValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public CoalesceValueTemplate(ValueTemplate left, ValueTemplate right)
        : base(MaxValueCount(left, right))
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    /// <summary>The value used unless it is <see langword="null"/>.</summary>
    public ValueTemplate Left { get; }

    /// <summary>The value used when <see cref="Left"/> is <see langword="null"/>.</summary>
    public ValueTemplate Right { get; }
}
