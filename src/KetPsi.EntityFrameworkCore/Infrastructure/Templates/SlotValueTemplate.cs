using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A value that does not read the row, computed in C# when the operator runs and sent as a parameter: a constant, a
/// captured variable, or an expression of them such as <c>limit + 1</c>. A <see langword="null"/> is written as
/// <c>NULL</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SlotValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public SlotValueTemplate(int valueIndex)
        : base(valueIndex + 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(valueIndex);

        ValueIndex = valueIndex;
    }

    /// <summary>The slot holding the value.</summary>
    public int ValueIndex { get; }
}
