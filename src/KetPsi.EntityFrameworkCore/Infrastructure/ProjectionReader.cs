using System.ComponentModel;
using System.Data.Common;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Creates one projected result from the current row: the body of a <c>Select</c> lambda with each entity property
/// read from its column. KetPsi.EntityFrameworkCore.Generator emits one per <c>Select</c> call.
/// </summary>
/// <typeparam name="T">The projected type, which may be an anonymous type.</typeparam>
/// <param name="reader">The data reader, positioned on a row.</param>
/// <param name="ordinals">For each name the projection was created with, the column ordinal to read.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate T ProjectionReader<out T>(DbDataReader reader, ReadOnlySpan<int> ordinals);
