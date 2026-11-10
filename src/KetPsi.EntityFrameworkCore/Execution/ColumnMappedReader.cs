using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Reads the results of SQL sent as it is, such as a stored procedure of <c>FromSql</c>, as if they had the columns of
/// a statement KetPsi wrote: ordinal <c>i</c> reads the column named <c>names[i]</c>, found by name ignoring case, as EF
/// Core finds it. Columns the SQL returns and no property reads are ignored.
/// </summary>
internal sealed class ColumnMappedReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly int[] _ordinals;

    /// <exception cref="InvalidOperationException">The results have no column for one of <paramref name="names"/>.</exception>
    public ColumnMappedReader(DbDataReader inner, IReadOnlyList<string> names)
    {
        _inner = inner;
        _ordinals = new int[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            _ordinals[i] = Find(inner, names[i]) ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.FromSqlMissingColumn, names[i]));
        }
    }

    public override int Depth => _inner.Depth;

    public override int FieldCount => _ordinals.Length;

    public override bool HasRows => _inner.HasRows;

    public override bool IsClosed => _inner.IsClosed;

    public override int RecordsAffected => _inner.RecordsAffected;

    public override object this[int ordinal] => _inner[_ordinals[ordinal]];

    public override object this[string name] => _inner[name];

    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(_ordinals[ordinal]);

    public override byte GetByte(int ordinal) => _inner.GetByte(_ordinals[ordinal]);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _inner.GetBytes(_ordinals[ordinal], dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => _inner.GetChar(_ordinals[ordinal]);

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _inner.GetChars(_ordinals[ordinal], dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(_ordinals[ordinal]);

    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(_ordinals[ordinal]);

    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(_ordinals[ordinal]);

    public override double GetDouble(int ordinal) => _inner.GetDouble(_ordinals[ordinal]);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(_ordinals[ordinal]);

    public override T GetFieldValue<T>(int ordinal) => _inner.GetFieldValue<T>(_ordinals[ordinal]);

    public override float GetFloat(int ordinal) => _inner.GetFloat(_ordinals[ordinal]);

    public override Guid GetGuid(int ordinal) => _inner.GetGuid(_ordinals[ordinal]);

    public override short GetInt16(int ordinal) => _inner.GetInt16(_ordinals[ordinal]);

    public override int GetInt32(int ordinal) => _inner.GetInt32(_ordinals[ordinal]);

    public override long GetInt64(int ordinal) => _inner.GetInt64(_ordinals[ordinal]);

    public override string GetName(int ordinal) => _inner.GetName(_ordinals[ordinal]);

    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    public override string GetString(int ordinal) => _inner.GetString(_ordinals[ordinal]);

    public override object GetValue(int ordinal) => _inner.GetValue(_ordinals[ordinal]);

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = Math.Min(values.Length, _ordinals.Length);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(_ordinals[ordinal]);

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

    // An exact match first, then one ignoring case.
    private static int? Find(DbDataReader reader, string name)
    {
        int? ignoringCase = null;
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var column = reader.GetName(i);
            if (string.Equals(column, name, StringComparison.Ordinal))
            {
                return i;
            }

            if (ignoringCase is null && string.Equals(column, name, StringComparison.OrdinalIgnoreCase))
            {
                ignoringCase = i;
            }
        }

        return ignoringCase;
    }
}
