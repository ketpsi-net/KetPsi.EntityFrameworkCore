namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>The clauses a dialect uses to apply <c>Skip</c> and <c>Take</c>.</summary>
/// <param name="SelectPrefix">Text placed right after <c>SELECT</c>, such as <c>TOP(@p0)</c>.</param>
/// <param name="Suffix">Text placed at the end of the statement, after <c>ORDER BY</c>.</param>
/// <param name="FallbackOrderBy">An <c>ORDER BY</c> expression to use when the query has none but the paging syntax requires one.</param>
public sealed record SqlPaging(string? SelectPrefix, string? Suffix, string? FallbackOrderBy)
{
    /// <summary>No paging.</summary>
    public static SqlPaging None { get; } = new(SelectPrefix: null, Suffix: null, FallbackOrderBy: null);
}
