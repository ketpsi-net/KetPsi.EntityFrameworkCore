namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>A complete SQL statement and its parameters, in placeholder order.</summary>
internal sealed record SqlStatement(string CommandText, IReadOnlyList<QueryParameter> Parameters);
