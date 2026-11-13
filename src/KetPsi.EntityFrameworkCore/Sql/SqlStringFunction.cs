namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>The SQL string functions the builder composes C#'s string methods from.</summary>
public enum SqlStringFunction
{
    /// <summary>The string in upper case: one argument.</summary>
    Upper,

    /// <summary>The string in lower case: one argument.</summary>
    Lower,

    /// <summary>The string without its leading spaces: one argument.</summary>
    TrimStart,

    /// <summary>The string without its trailing spaces: one argument.</summary>
    TrimEnd,

    /// <summary>The number of characters, as an <see cref="int"/>: one argument.</summary>
    Length,

    /// <summary>The part of the string at a one-based start, of a length: the string, the start, the length.</summary>
    Substring,

    /// <summary>The string with every occurrence of a string replaced: the string, the old and the new string.</summary>
    Replace,

    /// <summary>The one-based position of a string in another, or 0 when it is missing: the string looked for, then the string.</summary>
    Position,

    /// <summary>The first characters of the string: the string and their number.</summary>
    Left,

    /// <summary>The last characters of the string: the string and their number.</summary>
    Right,
}
