namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A predicate translated at build time. It references entity properties by name and reads its values
/// from numbered slots; table and column names are bound from the EF model at runtime.
/// </summary>
/// <remarks>
/// Instances are emitted by KetPsi.EntityFrameworkCore.Generator as static fields and are never mutated.
/// </remarks>
public abstract class PredicateTemplate
{
    private protected PredicateTemplate(int valueCount)
    {
        ValueCount = valueCount;
    }

    /// <summary>The number of value slots the predicate reads: one more than its highest value index.</summary>
    public int ValueCount { get; }

    private protected static void ValidatePropertyName(string propertyName)
        => ArgumentException.ThrowIfNullOrEmpty(propertyName);

    private protected static void ValidateValueIndex(int valueIndex)
        => ArgumentOutOfRangeException.ThrowIfNegative(valueIndex);
}
