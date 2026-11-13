namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The C# methods and members a <see cref="FunctionValueTemplate"/> can represent, written as EF Core writes them.</summary>
public enum ValueFunction
{
    /// <summary><c>s.ToUpper()</c>: one argument, the string.</summary>
    ToUpper,

    /// <summary><c>s.ToLower()</c>: one argument, the string.</summary>
    ToLower,

    /// <summary><c>s.Trim()</c>: one argument, the string.</summary>
    Trim,

    /// <summary><c>s.TrimStart()</c>: one argument, the string.</summary>
    TrimStart,

    /// <summary><c>s.TrimEnd()</c>: one argument, the string.</summary>
    TrimEnd,

    /// <summary><c>s.Length</c>: one argument, the string. SQL Server's <c>LEN</c> does not count trailing spaces, as in EF Core.</summary>
    Length,

    /// <summary><c>s.Substring(start)</c> or <c>s.Substring(start, length)</c>: the string, the zero-based start, and the length.</summary>
    Substring,

    /// <summary><c>s.Replace(oldValue, newValue)</c>: the string and the two strings.</summary>
    Replace,

    /// <summary><c>s.IndexOf(value)</c>: the string and the string or character looked for; -1 when it is missing.</summary>
    IndexOf,

    /// <summary><c>x.ToString()</c> of a string or an integer: one argument; a null integer is an empty string, as in C#.</summary>
    ToString,
}
