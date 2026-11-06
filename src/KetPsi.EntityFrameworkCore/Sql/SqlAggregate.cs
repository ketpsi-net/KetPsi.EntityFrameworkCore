namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>An aggregate function: <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c>.</summary>
public enum AggregateFunction
{
    /// <summary><c>Sum</c>: an empty set sums to zero.</summary>
    Sum,

    /// <summary><c>Average</c>.</summary>
    Average,

    /// <summary><c>Min</c>.</summary>
    Min,

    /// <summary><c>Max</c>.</summary>
    Max,
}

/// <summary>
/// The numeric type a <c>Sum</c> or <c>Average</c> overload works in, which decides the casts and the typed zero, as
/// in EF Core. <c>Min</c> and <c>Max</c> work in the column's own type.
/// </summary>
public enum AggregateValueType
{
    /// <summary>The column's own type: <c>Min</c> and <c>Max</c>.</summary>
    Column,

    /// <summary><see cref="int"/> or <c>int?</c>.</summary>
    Int32,

    /// <summary><see cref="long"/> or <c>long?</c>.</summary>
    Int64,

    /// <summary><see cref="decimal"/> or <c>decimal?</c>.</summary>
    Decimal,

    /// <summary><see cref="double"/> or <c>double?</c>.</summary>
    Double,

    /// <summary><see cref="float"/> or <c>float?</c>.</summary>
    Single,
}

/// <summary>
/// The aggregate a statement computes.
/// </summary>
/// <param name="Function">The aggregate function.</param>
/// <param name="PropertyName">
/// The entity property the selector reads, or <see langword="null"/> to aggregate the single column a projection
/// selects.
/// </param>
/// <param name="ValueType">The overload's numeric type.</param>
internal sealed record SqlAggregate(AggregateFunction Function, string? PropertyName, AggregateValueType ValueType);
