using System.Collections.Concurrent;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The SQL dialects available to KetPsi queries, keyed by EF Core provider name. Dialect packages
/// are registered by code that KetPsi.EntityFrameworkCore.Generator emits, so applications don't call this.
/// </summary>
public static class SqlDialects
{
    private static readonly ConcurrentDictionary<string, ISqlDialect> Dialects = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="dialect"/> for its provider. Registering the same dialect type again does nothing.</summary>
    /// <exception cref="InvalidOperationException">A dialect of a different type is already registered for the provider.</exception>
    public static void Register(ISqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentException.ThrowIfNullOrEmpty(dialect.ProviderName, nameof(dialect));

        var registered = Dialects.GetOrAdd(dialect.ProviderName, dialect);
        if (registered.GetType() != dialect.GetType())
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                ExceptionMessages.DialectAlreadyRegistered,
                dialect.ProviderName,
                registered.GetType(),
                dialect.GetType()));
        }
    }

    internal static ISqlDialect Resolve(string? providerName)
    {
        if (providerName is not null && Dialects.TryGetValue(providerName, out var dialect))
        {
            return dialect;
        }

        throw new InvalidOperationException(
            string.Format(CultureInfo.InvariantCulture, ExceptionMessages.DialectNotRegistered, providerName));
    }
}
