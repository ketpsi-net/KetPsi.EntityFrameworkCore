using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

internal static class OrderKey
{
    public static void Validate(ValueTemplate key, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != key.ValueCount)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.KeyValueCountMismatch, key.ValueCount, values.Length),
                nameof(values));
        }
    }
}
