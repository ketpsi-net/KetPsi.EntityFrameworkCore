using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.SqlServer;

/// <summary>SQL Server syntax for KetPsi queries.</summary>
public sealed class SqlServerDialect : ISqlDialect
{
    /// <summary>The EF Core provider name of Microsoft.EntityFrameworkCore.SqlServer.</summary>
    public const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    private const string ParameterPrefix = "@p";
    private const string NoOrderBy = "(SELECT 1)";

    // OPENJSON parses these into the matching SQL Server types.
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff";
    private const string DateTimeOffsetFormat = "yyyy-MM-ddTHH:mm:ss.fffffffzzz";
    private const string DateOnlyFormat = "yyyy-MM-dd";
    private const string TimeOnlyFormat = "HH:mm:ss.fffffff";
    private const string TimeSpanFormat = @"hh\:mm\:ss\.fffffff";

    private SqlServerDialect()
    {
    }

    /// <summary>The single instance.</summary>
    public static SqlServerDialect Instance { get; } = new();

    /// <inheritdoc/>
    public string ProviderName => SqlServerProviderName;

    /// <inheritdoc/>
    public string TrueLiteral => "CAST(1 AS bit)";

    /// <inheritdoc/>
    public string FalseLiteral => "CAST(0 AS bit)";

    /// <inheritdoc/>
    public string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <inheritdoc/>
    public string QualifyTable(string table, string? schema)
        => string.IsNullOrEmpty(schema)
            ? QuoteIdentifier(table)
            : QuoteIdentifier(schema) + "." + QuoteIdentifier(table);

    /// <inheritdoc/>
    public string ParameterName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return ParameterPrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public string Count(bool isLong) => isLong ? "COUNT_BIG(*)" : "COUNT(*)";

    /// <inheritdoc/>
    public string Exists(string subquery)
    {
        ArgumentException.ThrowIfNullOrEmpty(subquery);

        return $"SELECT CASE WHEN EXISTS ({subquery}) THEN {TrueLiteral} ELSE {FalseLiteral} END";
    }

    /// <inheritdoc/>
    public SqlPaging Paging(string? offsetParameter, string? limitParameter, bool hasOrderBy)
    {
        if (offsetParameter is null)
        {
            return limitParameter is null
                ? SqlPaging.None
                : new SqlPaging(SelectPrefix: $"TOP({limitParameter})", Suffix: null, FallbackOrderBy: null);
        }

        var suffix = limitParameter is null
            ? $"OFFSET {offsetParameter} ROWS"
            : $"OFFSET {offsetParameter} ROWS FETCH NEXT {limitParameter} ROWS ONLY";

        return new SqlPaging(SelectPrefix: null, suffix, FallbackOrderBy: hasOrderBy ? null : NoOrderBy);
    }

    /// <inheritdoc/>
    public string InList(string column, string listParameter, string storeType)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);
        ArgumentException.ThrowIfNullOrEmpty(listParameter);
        ArgumentException.ThrowIfNullOrEmpty(storeType);

        return $"{column} IN (SELECT [value] FROM OPENJSON({listParameter}) WITH ([value] {storeType} '$'))";
    }

    /// <inheritdoc/>
    public object CreateListParameterValue(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                WriteValue(writer, value);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case char character:
                writer.WriteStringValue(character.ToString());
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case Enum enumValue:
                WriteEnum(writer, enumValue);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture));
                break;
            case DateOnly date:
                writer.WriteStringValue(date.ToString(DateOnlyFormat, CultureInfo.InvariantCulture));
                break;
            case TimeOnly time:
                writer.WriteStringValue(time.ToString(TimeOnlyFormat, CultureInfo.InvariantCulture));
                break;
            case TimeSpan timeSpan:
                writer.WriteStringValue(timeSpan.ToString(TimeSpanFormat, CultureInfo.InvariantCulture));
                break;
            default:
                throw new NotSupportedException(
                    string.Format(CultureInfo.InvariantCulture, ExceptionMessages.UnsupportedListValueType, value.GetType()));
        }
    }

    // Enums without a value converter are stored as their underlying number.
    private static void WriteEnum(Utf8JsonWriter writer, Enum value)
    {
        if (Type.GetTypeCode(value.GetType()) == TypeCode.UInt64)
        {
            writer.WriteNumberValue(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
    }
}
