using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The generated entity readers. Code emitted by KetPsi.EntityFrameworkCore.Generator registers them;
/// applications don't call this.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityReaders
{
    // Included entities are only known by their runtime type, so they are looked up here.
    private static readonly ConcurrentDictionary<Type, EntityOperations> Operations = new();

    /// <summary>
    /// Registers the reader for <typeparamref name="T"/> unless one is already registered. Every assembly that
    /// queries an entity generates its own reader, and readers generated for the same entity are equivalent,
    /// so the first registration is kept.
    /// </summary>
    public static void Register<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IEntityReader<T> reader)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(reader);

        Interlocked.CompareExchange(ref Holder<T>.Reader, reader, comparand: null);
        Operations.TryAdd(typeof(T), new EntityOperations<T>(Holder<T>.Reader));
    }

    /// <exception cref="InvalidOperationException">No reader is registered for <typeparamref name="T"/>.</exception>
    internal static IEntityReader<T> Get<T>()
        => Holder<T>.Reader
            ?? throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.EntityReaderNotRegistered, typeof(T)));

    /// <exception cref="InvalidOperationException">No reader is registered for <paramref name="entityType"/>.</exception>
    internal static EntityOperations GetOperations(Type entityType)
        => Operations.TryGetValue(entityType, out var operations)
            ? operations
            : throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.EntityReaderNotRegistered, entityType));

    // One field per entity type: lookups need no dictionary and no runtime type keys.
    private static class Holder<T>
    {
        public static IEntityReader<T>? Reader;
    }
}

/// <summary>What included entities need from their typed reader, for code that only knows their runtime type.</summary>
internal abstract class EntityOperations
{
    public abstract IReadOnlyList<string> PropertyNames { get; }

    /// <summary>Creates an entity from the current row.</summary>
    public abstract object Read(DbDataReader row, ReadOnlySpan<int> ordinals);

    /// <summary>Returns the entity the context already tracks under <paramref name="key"/>, or <see langword="null"/>.</summary>
    public abstract object? FindTracked(DbContext context, object?[] key);
}

internal sealed class EntityOperations<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(IEntityReader<T> reader) : EntityOperations
    where T : class
{
    public override IReadOnlyList<string> PropertyNames => reader.PropertyNames;

    public override object Read(DbDataReader row, ReadOnlySpan<int> ordinals) => reader.Read(row, ordinals);

    public override object? FindTracked(DbContext context, object?[] key) => context.Set<T>().Local.FindEntryUntyped(key)?.Entity;
}
