namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>What the statement returns.</summary>
internal enum QueryTerminal
{
    /// <summary>Entity rows: <c>ToList</c>, <c>ToArray</c>, <c>First</c>, <c>Single</c> and their variants.</summary>
    Rows,

    /// <summary>An <see cref="int"/> row count: <c>Count</c>.</summary>
    Count,

    /// <summary>A <see cref="long"/> row count: <c>LongCount</c>.</summary>
    LongCount,

    /// <summary>Whether any row exists: <c>Any</c>.</summary>
    Any,
}
