using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Reads the columns of properties with a value converter as their CLR values, the way EF Core materializes them: the
/// column is read as the type the converter stores, then converted. Generated readers read columns by the property's
/// type, so every getter of a converted column returns the converted value in the type asked for; an enum stored as a
/// string is read by <see cref="GetInt32"/> as the enum's number, and a strongly typed key by
/// <see cref="GetFieldValue{T}"/>. A null in the column is read the way the stored type is, so a non-nullable property
/// throws the provider's own error, as in EF Core. Other columns are read as they are.
/// </summary>
internal sealed class ConvertingReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly IReadOnlyList<BoundProperty?> _converted;

    /// <param name="inner">The reader of the statement's rows.</param>
    /// <param name="converted">For each column, the property whose converter reads it, or <see langword="null"/>.</param>
    public ConvertingReader(DbDataReader inner, IReadOnlyList<BoundProperty?> converted)
    {
        _inner = inner;
        _converted = converted;
    }

    public override int Depth => _inner.Depth;

    public override int FieldCount => _inner.FieldCount;

    public override bool HasRows => _inner.HasRows;

    public override bool IsClosed => _inner.IsClosed;

    public override int RecordsAffected => _inner.RecordsAffected;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => _inner[name];

    public override bool GetBoolean(int ordinal) => Converted(ordinal) is { } p ? Read<bool>(p, ordinal) : _inner.GetBoolean(ordinal);

    public override byte GetByte(int ordinal) => Converted(ordinal) is { } p ? Read<byte>(p, ordinal) : _inner.GetByte(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => Converted(ordinal) is { } p ? Read<char>(p, ordinal) : _inner.GetChar(ordinal);

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);

    public override DateTime GetDateTime(int ordinal) => Converted(ordinal) is { } p ? Read<DateTime>(p, ordinal) : _inner.GetDateTime(ordinal);

    public override decimal GetDecimal(int ordinal) => Converted(ordinal) is { } p ? Read<decimal>(p, ordinal) : _inner.GetDecimal(ordinal);

    public override double GetDouble(int ordinal) => Converted(ordinal) is { } p ? Read<double>(p, ordinal) : _inner.GetDouble(ordinal);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);

    public override T GetFieldValue<T>(int ordinal) => Converted(ordinal) is { } p ? Read<T>(p, ordinal) : _inner.GetFieldValue<T>(ordinal);

    public override float GetFloat(int ordinal) => Converted(ordinal) is { } p ? Read<float>(p, ordinal) : _inner.GetFloat(ordinal);

    public override Guid GetGuid(int ordinal) => Converted(ordinal) is { } p ? Read<Guid>(p, ordinal) : _inner.GetGuid(ordinal);

    public override short GetInt16(int ordinal) => Converted(ordinal) is { } p ? Read<short>(p, ordinal) : _inner.GetInt16(ordinal);

    public override int GetInt32(int ordinal) => Converted(ordinal) is { } p ? Read<int>(p, ordinal) : _inner.GetInt32(ordinal);

    public override long GetInt64(int ordinal) => Converted(ordinal) is { } p ? Read<long>(p, ordinal) : _inner.GetInt64(ordinal);

    public override string GetName(int ordinal) => _inner.GetName(ordinal);

    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    public override string GetString(int ordinal) => Converted(ordinal) is { } p ? Read<string>(p, ordinal) : _inner.GetString(ordinal);

    /// <summary>The column's CLR value; <see cref="DBNull"/> for a null the converter does not read.</summary>
    public override object GetValue(int ordinal)
    {
        if (Converted(ordinal) is not { } property)
        {
            return _inner.GetValue(ordinal);
        }

        return _inner.IsDBNull(ordinal) && !property.Converter!.ConvertsNulls
            ? DBNull.Value
            : property.FromProvider(ReadStored(property, ordinal)) ?? DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    public override bool NextResult() => _inner.NextResult();

    public override bool Read() => _inner.Read();

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => _inner.ReadAsync(cancellationToken);

    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();

    private BoundProperty? Converted(int ordinal) => ordinal < _converted.Count ? _converted[ordinal] : null;

    private T Read<T>(BoundProperty property, int ordinal)
    {
        var value = property.FromProvider(ReadStored(property, ordinal));
        if (value is T typed)
        {
            return typed;
        }

        // A generated reader reads an enum as its underlying number, and casts it back.
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return target.IsEnum
            ? (T)Enum.ToObject(target, value!)
            : (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture)!;
    }

    // The stored value, read by the getter of the type the converter stores, which throws the provider's own error
    // for a null, as EF Core's materializer does; a converter that reads nulls gets null.
    private object? ReadStored(BoundProperty property, int ordinal)
    {
        var converter = property.Converter!;
        if (converter.ConvertsNulls && _inner.IsDBNull(ordinal))
        {
            return null;
        }

        var stored = Nullable.GetUnderlyingType(converter.ProviderClrType) ?? converter.ProviderClrType;
        return stored switch
        {
            _ when stored == typeof(string) => _inner.GetString(ordinal),
            _ when stored == typeof(int) => _inner.GetInt32(ordinal),
            _ when stored == typeof(long) => _inner.GetInt64(ordinal),
            _ when stored == typeof(short) => _inner.GetInt16(ordinal),
            _ when stored == typeof(byte) => _inner.GetByte(ordinal),
            _ when stored == typeof(bool) => _inner.GetBoolean(ordinal),
            _ when stored == typeof(decimal) => _inner.GetDecimal(ordinal),
            _ when stored == typeof(double) => _inner.GetDouble(ordinal),
            _ when stored == typeof(float) => _inner.GetFloat(ordinal),
            _ when stored == typeof(DateTime) => _inner.GetDateTime(ordinal),
            _ when stored == typeof(Guid) => _inner.GetGuid(ordinal),
            _ when stored == typeof(DateTimeOffset) => _inner.GetFieldValue<DateTimeOffset>(ordinal),
            _ when stored == typeof(TimeSpan) => _inner.GetFieldValue<TimeSpan>(ordinal),
            _ when stored == typeof(DateOnly) => _inner.GetFieldValue<DateOnly>(ordinal),
            _ when stored == typeof(TimeOnly) => _inner.GetFieldValue<TimeOnly>(ordinal),
            _ when stored == typeof(byte[]) => _inner.GetFieldValue<byte[]>(ordinal),
            _ => _inner.GetValue(ordinal),
        };
    }
}
