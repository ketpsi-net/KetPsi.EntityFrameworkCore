namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>A complete SQL statement and its parameters, in placeholder order.</summary>
/// <param name="CommandText">The SQL.</param>
/// <param name="Parameters">The parameters.</param>
/// <param name="ColumnsByName">
/// For SQL of <c>FromSql</c> sent as it is, the column names of the entity's properties in model order, which are
/// read by name, as EF Core reads them; <see langword="null"/> when the statement selects the columns in that order.
/// </param>
internal sealed record SqlStatement(string CommandText, IReadOnlyList<QueryParameter> Parameters, IReadOnlyList<string>? ColumnsByName = null);
