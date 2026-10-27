using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>A value passed to the statement as a parameter.</summary>
/// <param name="Name">The placeholder used in the SQL.</param>
/// <param name="Value">The value. Never <see langword="null"/>: null comparisons are written as <c>IS NULL</c> instead.</param>
/// <param name="Property">
/// The property the value is compared with, which decides the parameter's type; <see langword="null"/> for
/// values that are not compared with a property, such as paging counts and <c>Contains</c> lists.
/// </param>
internal sealed record QueryParameter(string Name, object Value, BoundProperty? Property);
