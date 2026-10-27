using System.ComponentModel;
using System.Data.Common;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Materializes entities from data reader rows without reflection. KetPsi.EntityFrameworkCore.Generator
/// emits one implementation per entity type.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEntityReader<out T>
{
    /// <summary>The entity properties the reader sets, in the order of the ordinals passed to <see cref="Read"/>.</summary>
    IReadOnlyList<string> PropertyNames { get; }

    /// <summary>Creates an entity from the current row.</summary>
    /// <param name="reader">The data reader, positioned on a row.</param>
    /// <param name="ordinals">
    /// For each name in <see cref="PropertyNames"/>, the column ordinal to read, or -1 when the model does not map
    /// the property and the reader must leave it unset.
    /// </param>
    T Read(DbDataReader reader, ReadOnlySpan<int> ordinals);
}
