namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The C# comparison operators a <see cref="ComparisonTemplate"/> can represent.</summary>
public enum ComparisonOperator
{
    /// <summary><c>==</c>, or <c>Equals</c>.</summary>
    Equal,

    /// <summary><c>!=</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>.</summary>
    LessThan,

    /// <summary><c>&lt;=</c>.</summary>
    LessThanOrEqual,

    /// <summary><c>&gt;</c>.</summary>
    GreaterThan,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterThanOrEqual,
}
