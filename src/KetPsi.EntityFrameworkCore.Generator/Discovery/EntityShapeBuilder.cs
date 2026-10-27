using System.Text;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Describes how to materialize an entity from its CLR shape. Which properties EF Core actually maps is decided
/// at runtime from the model; the reader skips the ones the model does not map.
/// </summary>
internal static class EntityShapeBuilder
{
    private const string NotMappedAttribute = "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute";
    private const string ReaderSuffix = "_Reader";

    public static EntityShape Build(INamedTypeSymbol entity, Compilation compilation, LocationInfo fallbackLocation)
    {
        var typeName = EntityDiscovery.TypeName(entity);
        var diagnostics = new List<DiagnosticInfo>();

        var construction = GetConstruction(entity, compilation);
        if (construction == Construction.None)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.EntityNotConstructible, LocationOf(entity, fallbackLocation), entity.ToDisplayString()));
        }

        var properties = new List<PropertyShape>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var type = entity; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                // A derived declaration hides a base one with the same name.
                if (member is IPropertySymbol property && seen.Add(property.Name)
                    && BuildProperty(entity, property, compilation, construction, fallbackLocation, diagnostics) is { } shape)
                {
                    properties.Add(shape);
                }
            }
        }

        return new EntityShape(
            typeName,
            ReaderName(typeName),
            construction,
            properties.ToEquatableArray(),
            diagnostics.ToEquatableArray());
    }

    internal static string ReaderName(string typeName)
    {
        var name = new StringBuilder();
        foreach (var character in typeName.Replace("global::", ""))
        {
            name.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return name.Append(ReaderSuffix).ToString();
    }

    private static Construction GetConstruction(INamedTypeSymbol entity, Compilation compilation)
    {
        if (entity.IsAbstract || entity.IsStatic || entity.TypeKind != TypeKind.Class)
        {
            return Construction.None;
        }

        var constructor = entity.InstanceConstructors.FirstOrDefault(static c => c.Parameters.Length == 0);
        if (constructor is null)
        {
            return Construction.None;
        }

        return compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly) ? Construction.New : Construction.Accessor;
    }

    private static PropertyShape? BuildProperty(
        INamedTypeSymbol entity,
        IPropertySymbol property,
        Compilation compilation,
        Construction construction,
        LocationInfo fallbackLocation,
        List<DiagnosticInfo> diagnostics)
    {
        // EF Core maps settable instance properties by convention; [NotMapped] opts out.
        if (property.IsStatic || property.IsIndexer || property.SetMethod is null
            || property.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == NotMappedAttribute))
        {
            return null;
        }

        var (read, valueType, canBeNull, isScalar) = Classify(property.Type);
        if (!isScalar)
        {
            // Navigations, collections and complex types are not columns of the entity's table.
            return null;
        }

        if (read is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.PropertyTypeNotSupported,
                LocationOf(property, fallbackLocation),
                entity.ToDisplayString(),
                property.Name,
                property.Type.ToDisplayString()));
            return null;
        }

        var setter = GetSetter(property.SetMethod, compilation);
        if (setter == Setter.Init && construction == Construction.Accessor)
        {
            // Init accessors can only be called from an object initializer, which needs an accessible constructor.
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.EntityNotConstructible, LocationOf(entity, fallbackLocation), entity.ToDisplayString()));
            return null;
        }

        return new PropertyShape(
            property.Name,
            EntityDiscovery.TypeName(property.Type),
            EntityDiscovery.TypeName(property.SetMethod.ContainingType),
            read.Value,
            valueType,
            canBeNull,
            setter);
    }

    private static Setter GetSetter(IMethodSymbol setMethod, Compilation compilation)
    {
        if (!compilation.IsSymbolAccessibleWithin(setMethod, compilation.Assembly))
        {
            return Setter.Accessor;
        }

        return setMethod.IsInitOnly ? Setter.Init : Setter.Direct;
    }

    /// <summary>How to read a property type: the reader call, the value type it reads, and whether DBNull is possible.</summary>
    /// <returns><c>isScalar</c> is false for types that are not columns; <c>read</c> is null for scalar types that cannot be read yet.</returns>
    private static (ReadMethod? Read, string? ValueType, bool CanBeNull, bool IsScalar) Classify(ITypeSymbol type)
    {
        var canBeNull = !type.IsValueType;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
            canBeNull = true;
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            return (ReadMethod.GetFieldValue, "byte[]", canBeNull, true);
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return (IntegerRead(enumType.EnumUnderlyingType!), EntityDiscovery.TypeName(enumType.EnumUnderlyingType!), canBeNull, true);
        }

        var simple = type.SpecialType switch
        {
            SpecialType.System_Boolean => ReadMethod.GetBoolean,
            SpecialType.System_Byte => ReadMethod.GetByte,
            SpecialType.System_Int16 => ReadMethod.GetInt16,
            SpecialType.System_Int32 => ReadMethod.GetInt32,
            SpecialType.System_Int64 => ReadMethod.GetInt64,
            SpecialType.System_Single => ReadMethod.GetFloat,
            SpecialType.System_Double => ReadMethod.GetDouble,
            SpecialType.System_Decimal => ReadMethod.GetDecimal,
            SpecialType.System_String => ReadMethod.GetString,
            SpecialType.System_DateTime => ReadMethod.GetDateTime,
            _ => (ReadMethod?)null,
        };
        if (simple is not null)
        {
            return (simple, null, canBeNull, true);
        }

        switch (type.ToDisplayString())
        {
            case "System.Guid":
                return (ReadMethod.GetGuid, null, canBeNull, true);
            case "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan":
                return (ReadMethod.GetFieldValue, EntityDiscovery.TypeName(type), canBeNull, true);
        }

        // Other value types are scalar columns this milestone cannot read; reference types are navigations.
        return (null, null, canBeNull, type.IsValueType);
    }

    private static ReadMethod? IntegerRead(ITypeSymbol underlying) => underlying.SpecialType switch
    {
        SpecialType.System_Byte => ReadMethod.GetByte,
        SpecialType.System_Int16 => ReadMethod.GetInt16,
        SpecialType.System_Int32 => ReadMethod.GetInt32,
        SpecialType.System_Int64 => ReadMethod.GetInt64,
        _ => null,
    };

    private static LocationInfo LocationOf(ISymbol symbol, LocationInfo fallback)
        => symbol.Locations.FirstOrDefault(static l => l.IsInSource) is { } location ? LocationInfo.From(location) : fallback;
}
