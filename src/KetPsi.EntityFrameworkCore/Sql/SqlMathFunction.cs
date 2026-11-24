namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>The SQL math functions the builder composes C#'s <see cref="Math"/> methods from.</summary>
public enum SqlMathFunction
{
    /// <summary>The absolute value: one argument.</summary>
    Abs,

    /// <summary>The value rounded half away from zero: the value and the number of decimals.</summary>
    Round,

    /// <summary>The value with its fraction dropped: one argument.</summary>
    Truncate,

    /// <summary>The largest integer not above the value: one argument.</summary>
    Floor,

    /// <summary>The smallest integer not below the value: one argument.</summary>
    Ceiling,

    /// <summary>A value raised to a power: the value, then the power.</summary>
    Power,

    /// <summary>The square root: one argument.</summary>
    Sqrt,

    /// <summary>The natural logarithm: one argument.</summary>
    Log,

    /// <summary>The base 10 logarithm: one argument.</summary>
    Log10,

    /// <summary>e raised to the value: one argument.</summary>
    Exp,

    /// <summary>-1, 0 or 1 for a negative, zero or positive value, in the value's type: one argument.</summary>
    Sign,

    /// <summary>The sine: one argument, in radians.</summary>
    Sin,

    /// <summary>The cosine: one argument, in radians.</summary>
    Cos,

    /// <summary>The tangent: one argument, in radians.</summary>
    Tan,

    /// <summary>The arcsine: one argument.</summary>
    Asin,

    /// <summary>The arccosine: one argument.</summary>
    Acos,

    /// <summary>The arctangent: one argument.</summary>
    Atan,

    /// <summary>The angle of a point: its y, then its x.</summary>
    Atan2,
}
