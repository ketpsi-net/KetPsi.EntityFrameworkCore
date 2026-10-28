using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>
/// Binds entity types to tables and columns from EF Core's model, so the fluent configuration stays the
/// single source of truth. Results are cached per model instance.
/// </summary>
internal static class ModelBinder
{
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<Type, BoundEntity>> Cache = new();

    /// <exception cref="InvalidOperationException">The type is not an entity in the model, or a property has no column.</exception>
    /// <exception cref="NotSupportedException">The entity uses a mapping KetPsi.EntityFrameworkCore does not support yet.</exception>
    public static BoundEntity GetEntity(IModel model, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(clrType);

        var entities = Cache.GetValue(model, static _ => new ConcurrentDictionary<Type, BoundEntity>());
        return entities.GetOrAdd(clrType, static (type, m) => Bind(m, type), model);
    }

    private static BoundEntity Bind(IModel model, Type clrType)
    {
        // FindEntityType(Type) carries trimming annotations that a runtime Type cannot satisfy.
        var entityType = model.GetEntityTypes().FirstOrDefault(e => e.ClrType == clrType && !e.HasSharedClrType)
            ?? throw new InvalidOperationException(Format(ExceptionMessages.EntityNotInModel, clrType));

        if (entityType.BaseType is not null || entityType.GetDerivedTypes().Any())
        {
            throw new NotSupportedException(Format(ExceptionMessages.EntityInheritanceNotSupported, clrType));
        }

        if (entityType.IsOwned() || entityType.GetNavigations().Any(static n => n.TargetEntityType.IsOwned()))
        {
            throw new NotSupportedException(Format(ExceptionMessages.OwnedEntityNotSupported, clrType));
        }

        if (entityType.GetComplexProperties().Any())
        {
            throw new NotSupportedException(Format(ExceptionMessages.ComplexPropertyNotSupported, clrType));
        }

        var table = entityType.GetTableName()
            ?? throw new NotSupportedException(Format(ExceptionMessages.EntityNotMappedToTable, clrType));
        var schema = entityType.GetSchema();
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        var properties = entityType.GetProperties()
            .Select(property => BindProperty(property, storeObject, clrType, table))
            .ToArray();

        return new BoundEntity(clrType, table, schema, properties, GetPrimaryKey(entityType, properties), GetFilterKeys(entityType));
    }

    // In key order, which is the order EF Core's identity map expects key values in. Keyless types have none.
    private static BoundProperty[]? GetPrimaryKey(IEntityType entityType, BoundProperty[] properties)
        => entityType.FindPrimaryKey()?.Properties
            .Select(key => properties.First(p => p.Name == key.Name))
            .ToArray();

    // EF Core 10 added named filters; earlier versions allow one unnamed filter per entity.
    private static string?[] GetFilterKeys(IEntityType entityType)
#if NET10_0_OR_GREATER
        => entityType.GetDeclaredQueryFilters().Select(static f => f.IsAnonymous ? null : f.Key).ToArray();
#else
        => entityType.GetQueryFilter() is null ? [] : [null];
#endif

    private static BoundProperty BindProperty(IProperty property, StoreObjectIdentifier storeObject, Type entityClrType, string table)
    {
        if (property.GetValueConverter() is not null)
        {
            throw new NotSupportedException(Format(ExceptionMessages.ValueConverterNotSupported, entityClrType, property.Name));
        }

        var column = property.GetColumnName(storeObject);
        var storeType = property.GetColumnType(storeObject);
        if (column is null || storeType is null)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.PropertyColumnMissing, entityClrType, property.Name, table));
        }

        return new BoundProperty(
            property.Name, column, storeType, property.IsColumnNullable(storeObject), property.ClrType, property.IsShadowProperty());
    }

    private static string Format(string format, Type clrType) => string.Format(CultureInfo.InvariantCulture, format, clrType);

    private static string Format(string format, Type clrType, string member)
        => string.Format(CultureInfo.InvariantCulture, format, clrType, member);
}
