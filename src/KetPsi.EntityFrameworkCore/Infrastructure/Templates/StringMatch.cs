namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The string tests a <see cref="StringMatchTemplate"/> can represent.</summary>
public enum StringMatch
{
    /// <summary><c>s.Contains(part)</c>.</summary>
    Contains,

    /// <summary><c>s.StartsWith(part)</c>.</summary>
    StartsWith,

    /// <summary><c>s.EndsWith(part)</c>.</summary>
    EndsWith,
}
