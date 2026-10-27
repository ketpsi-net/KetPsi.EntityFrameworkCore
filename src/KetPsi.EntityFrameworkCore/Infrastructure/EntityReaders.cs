using System.ComponentModel;
using System.Globalization;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The generated entity readers. Code emitted by KetPsi.EntityFrameworkCore.Generator registers them;
/// applications don't call this.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityReaders
{
    /// <summary>
    /// Registers the reader for <typeparamref name="T"/> unless one is already registered. Every assembly that
    /// queries an entity generates its own reader, and readers generated for the same entity are equivalent,
    /// so the first registration is kept.
    /// </summary>
    public static void Register<T>(IEntityReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Interlocked.CompareExchange(ref Holder<T>.Reader, reader, comparand: null);
    }

    /// <exception cref="InvalidOperationException">No reader is registered for <typeparamref name="T"/>.</exception>
    internal static IEntityReader<T> Get<T>()
        => Holder<T>.Reader
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.EntityReaderNotRegistered, typeof(T)));

    // One field per entity type: lookups need no dictionary and no runtime type keys.
    private static class Holder<T>
    {
        public static IEntityReader<T>? Reader;
    }
}
