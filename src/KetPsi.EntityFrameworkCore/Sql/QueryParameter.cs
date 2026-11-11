using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>A value passed to the statement as a parameter.</summary>
/// <param name="Name">The placeholder used in the SQL.</param>
/// <param name="Value">
/// The value, as the column holds it: a value compared with a property that has a value converter is already converted. Never <see langword="null"/>: null comparisons are written as <c>IS NULL</c> instead, and a null argument
/// of <c>FromSql</c> is <see cref="DBNull.Value"/>. A <see cref="System.Data.Common.DbParameter"/> argument of
/// <c>FromSql</c> is the parameter itself, sent as it is.
/// </param>
/// <param name="Property">
/// The property the value is compared with, which decides the parameter's type; <see langword="null"/> for
/// values that are not compared with a property, such as paging counts and <c>Contains</c> lists.
/// </param>
internal sealed record QueryParameter(string Name, object Value, BoundProperty? Property);
