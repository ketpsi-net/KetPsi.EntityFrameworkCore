using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A C# conversion, explicit or implicit: <c>(double)x.Quantity</c>, or the <see cref="long"/> an <see cref="int"/>
/// becomes in <c>x.Quantity * x.Quantity &gt; x.Views</c>. Written as a <c>CAST</c> where SQL needs one, as EF Core
/// writes it: widening to <see cref="int"/>, and conversions between an enum and its number, need none.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ConvertValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="operand">The converted value.</param>
    /// <param name="type">The type converted to; a nullable type converts to its underlying type.</param>
    public ConvertValueTemplate(ValueTemplate operand, Type type)
        : base(MaxValueCount(operand))
    {
        ArgumentNullException.ThrowIfNull(operand);
        ArgumentNullException.ThrowIfNull(type);

        Operand = operand;
        Type = Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>The converted value.</summary>
    public ValueTemplate Operand { get; }

    /// <summary>The type converted to, never nullable.</summary>
    public Type Type { get; }
}
