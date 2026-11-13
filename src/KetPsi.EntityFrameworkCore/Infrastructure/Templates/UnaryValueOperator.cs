namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The C# unary operators a <see cref="UnaryValueTemplate"/> can represent.</summary>
public enum UnaryValueOperator
{
    /// <summary><c>-x</c>.</summary>
    Negate,

    /// <summary><c>~x</c> on integers.</summary>
    BitwiseNot,
}
