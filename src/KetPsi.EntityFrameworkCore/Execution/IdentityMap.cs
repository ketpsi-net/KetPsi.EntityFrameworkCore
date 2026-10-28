using System.Data.Common;
using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Resolves identities within one execution of an <c>AsNoTrackingWithIdentityResolution</c> query, as EF Core does:
/// rows with the same key return the instance materialized from the first of them, and nothing is tracked.
/// </summary>
internal sealed class IdentityMap<T>(BoundEntity entity)
    where T : class
{
    private readonly KeyReader _key = new(entity);
    private readonly Dictionary<object?[], T> _entities = new(KeyReader.Comparer);

    /// <summary>Returns the entity already read under the current row's key, or materializes it.</summary>
    public T Resolve(DbDataReader row, Func<DbDataReader, T> materialize)
    {
        var key = _key.Read(row);
        if (!_entities.TryGetValue(key, out var entity))
        {
            entity = materialize(row);
            _entities.Add(key, entity);
        }

        return entity;
    }
}
