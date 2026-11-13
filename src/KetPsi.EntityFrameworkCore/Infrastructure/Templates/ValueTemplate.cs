namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A value computed in SQL, translated at build time: an entity property, a value read from a slot, or an operation on
/// other values. Predicates compare values and orderings sort by them; property names are bound from the EF model at
/// runtime.
/// </summary>
/// <remarks>
/// Instances are emitted by KetPsi.EntityFrameworkCore.Generator as static fields and are never mutated.
/// </remarks>
public abstract class ValueTemplate
{
    private protected ValueTemplate(int valueCount)
    {
        ValueCount = valueCount;
    }

    /// <summary>The number of value slots the value reads: one more than its highest value index.</summary>
    public int ValueCount { get; }

    private protected static int MaxValueCount(params ReadOnlySpan<ValueTemplate?> operands)
    {
        var count = 0;
        foreach (var operand in operands)
        {
            count = Math.Max(count, operand?.ValueCount ?? 0);
        }

        return count;
    }
}
