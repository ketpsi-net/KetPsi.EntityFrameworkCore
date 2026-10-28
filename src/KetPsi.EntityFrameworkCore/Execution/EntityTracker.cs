using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using KetPsi.EntityFrameworkCore.Binding;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Hands rows of a tracking query to EF Core's change tracker, the way EF Core's own queries do: an entity already
/// tracked under the row's key is returned as it is, without being refreshed; any other row is materialized, given its
/// shadow values, and attached as <see cref="EntityState.Unchanged"/>, which also runs EF Core's navigation fixup.
/// </summary>
internal sealed class EntityTracker<[DynamicallyAccessedMembers(EntityMembers.Required)] T>
    where T : class
{
    private readonly DbContext _context;
    private readonly KeyReader _key;
    private readonly (string Name, int Ordinal, Type ClrType)[] _shadowProperties;

    /// <param name="context">The context whose change tracker receives the entities.</param>
    /// <param name="entity">The bound entity; it must have a primary key.</param>
    public EntityTracker(DbContext context, BoundEntity entity)
    {
        _context = context;
        _key = new KeyReader(entity);
        _shadowProperties = [.. entity.ShadowProperties.Select(p => (p.Name, KeyReader.Ordinal(entity, p), p.ClrType))];
    }

    /// <summary>Returns the tracked entity for the current row, materializing and attaching it when it is new.</summary>
    public T Track(DbDataReader row, Func<DbDataReader, T> materialize)
    {
        if (_context.Set<T>().Local.FindEntryUntyped(_key.Read(row)) is { } existing)
        {
            return existing.Entity;
        }

        var entity = materialize(row);
        var entry = _context.Entry(entity);

        // Shadow values must be in place before attaching, so EF Core snapshots them as original values.
        foreach (var (name, ordinal, clrType) in _shadowProperties)
        {
            entry.Property(name).CurrentValue = KeyReader.ToClrValue(row.GetValue(ordinal), clrType);
        }

        entry.State = EntityState.Unchanged;
        return entity;
    }
}
