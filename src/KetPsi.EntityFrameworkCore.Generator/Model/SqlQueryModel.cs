namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>
/// The result columns of <c>Database.SqlQuery</c>, besides the call's projection, which reads them: the CLR type of each
/// column, and whether the result is a scalar.
/// </summary>
/// <param name="ColumnTypes">The fully qualified CLR type of each column, for <c>typeof</c>.</param>
/// <param name="ReadsFirstColumn">Whether the result is a scalar, read from the first column of SQL sent as it is.</param>
internal sealed record SqlQueryModel(EquatableArray<string> ColumnTypes, bool ReadsFirstColumn);
