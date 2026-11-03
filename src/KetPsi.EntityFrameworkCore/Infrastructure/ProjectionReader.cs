using System.ComponentModel;
using System.Data.Common;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Creates one projected result from the current row: the body of a <c>Select</c> lambda with each entity property
/// read from its column and each projected entity taken from <paramref name="entities"/>.
/// KetPsi.EntityFrameworkCore.Generator emits one per <c>Select</c> call.
/// </summary>
/// <typeparam name="T">The projected type, which may be an anonymous type.</typeparam>
/// <param name="reader">The data reader, positioned on a row.</param>
/// <param name="ordinals">For each property path the projection was created with, the column ordinal to read.</param>
/// <param name="entities">
/// For each entity path the projection was created with, the entity read from the row: tracked, identity-resolved and
/// with its included navigations loaded as the query asks, or <see langword="null"/> when an optional navigation
/// found none.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate T ProjectionReader<out T>(DbDataReader reader, ReadOnlySpan<int> ordinals, ReadOnlySpan<object?> entities);
