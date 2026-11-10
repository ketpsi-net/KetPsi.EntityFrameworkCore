using System.Data.Common;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The SQL a query starts from: <c>FromSql</c>, <c>FromSqlInterpolated</c> or <c>FromSqlRaw</c>. Each <c>{0}</c>,
/// <c>{1}</c> placeholder becomes a parameter, as in EF Core, or the name of a <see cref="DbParameter"/> argument.
/// </summary>
/// <param name="format">The SQL with its placeholders.</param>
/// <param name="arguments">The values or <see cref="DbParameter"/>s of the placeholders, by index.</param>
/// <param name="alwaysFormat">
/// Whether the format is formatted without arguments too, as an interpolated string is; <c>FromSqlRaw</c> without
/// arguments sends the SQL as written.
/// </param>
internal sealed class RawSql(string format, IReadOnlyList<object?> arguments, bool alwaysFormat)
{
    public string Format { get; } = format;

    public IReadOnlyList<object?> Arguments { get; } = arguments;

    public bool AlwaysFormat { get; } = alwaysFormat;

    /// <summary>The names of the <see cref="DbParameter"/> arguments, which the query's own parameters must not take.</summary>
    public IEnumerable<string> ParameterNames => Arguments.OfType<DbParameter>().Select(static p => p.ParameterName);

    /// <summary>
    /// Whether the SQL can be composed over, as EF Core decides it: its first token, after whitespace and comments, is
    /// <c>SELECT</c>. A stored procedure call or a common table expression can only run as it is.
    /// </summary>
    public static bool IsComposable(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i]))
            {
                i++;
            }
            else if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOf('\n', i);
                if (end < 0)
                {
                    return false;
                }

                i = end + 1;
            }
            else if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return false;
                }

                i = end + 2;
            }
            else
            {
                break;
            }
        }

        const string Select = "SELECT";
        return sql.Length - i >= Select.Length
            && string.Compare(sql, i, Select, 0, Select.Length, StringComparison.OrdinalIgnoreCase) == 0
            && (sql.Length == i + Select.Length || !(char.IsLetterOrDigit(sql[i + Select.Length]) || sql[i + Select.Length] == '_'));
    }
}
