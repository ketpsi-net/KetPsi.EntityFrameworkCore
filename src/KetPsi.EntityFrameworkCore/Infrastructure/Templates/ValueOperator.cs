namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The C# binary operators a <see cref="BinaryValueTemplate"/> can represent.</summary>
public enum ValueOperator
{
    /// <summary><c>+</c> on numbers.</summary>
    Add,

    /// <summary><c>-</c>.</summary>
    Subtract,

    /// <summary><c>*</c>.</summary>
    Multiply,

    /// <summary><c>/</c>; integer operands divide as integers, as in C#.</summary>
    Divide,

    /// <summary><c>%</c>.</summary>
    Modulo,

    /// <summary><c>&amp;</c> on integers.</summary>
    BitwiseAnd,

    /// <summary><c>|</c> on integers.</summary>
    BitwiseOr,

    /// <summary><c>^</c> on integers or booleans.</summary>
    ExclusiveOr,

    /// <summary><c>+</c> on strings, where C# reads a <see langword="null"/> operand as an empty string.</summary>
    Concat,
}
