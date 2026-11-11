using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// The result type of <c>Database.SqlQuery</c>, as EF Core reads it: a scalar from the column <c>Value</c>, or a class or
/// record from the columns named like its properties, set through an object initializer or a constructor whose
/// parameters are its properties. Navigations are not columns, so EF Core rejects them, and so does KetPsi.
/// </summary>
internal static class SqlQueryTranslator
{
    /// <summary>The column a scalar result is read from when a query composes over the SQL.</summary>
    public const string ScalarColumn = "Value";

    public static (SqlQueryTranslation? Translation, string? Reason) Translate(ITypeSymbol result, Compilation compilation, LocationInfo location)
    {
        if (!compilation.IsSymbolAccessibleWithin(result, compilation.Assembly))
        {
            return (null, "the type must be accessible to generated code in the same assembly");
        }

        // SqlQuery<T> applies no value converter, so a type only a converter can store can't be read, as in EF Core.
        if (EntityShapeBuilder.NeedsConverter(result))
        {
            return (null, $"'{result.ToDisplayString()}' is stored through a value converter, which SqlQuery does not apply");
        }

        if (EntityShapeBuilder.IsReadableScalar(result))
        {
            return (Scalar(result, compilation), null);
        }

        // A collection is a sequence of values, not a row with columns.
        if (result is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } type
            || type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
        {
            return (null, "it must be a scalar, or a class or record whose properties are the columns");
        }

        var shape = EntityShapeBuilder.Build(type, compilation, location);
        // A record's EqualityContract is the compiler's, not a member of the result.
        if (shape.Navigations.FirstOrDefault(n => FindProperty(type, n.Name) is { IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public or Accessibility.Internal })
            is { } navigation)
        {
            return (null, $"'{navigation.Name}' is a navigation, which SqlQuery cannot read, as in EF Core; query the entity with FromSql instead");
        }

        if (shape.Properties.Count == 0)
        {
            return (null, "it has no settable properties to read the columns into");
        }

        var properties = shape.Properties.Select(p => (Shape: p, Symbol: FindProperty(type, p.Name)!)).ToList();
        if (properties.FirstOrDefault(static p => EntityShapeBuilder.NeedsConverter(p.Symbol.Type)) is { Symbol: { } converted })
        {
            return (null, $"'{converted.Name}' has the type '{converted.Type.ToDisplayString()}', which only a value converter can store, and SqlQuery applies none");
        }
        var reads = properties.Select((p, i) => Read(p.Shape, p.Symbol.Type, i)).ToList();

        string construction;
        if (shape.Construction == Construction.New)
        {
            if (properties.FirstOrDefault(static p => p.Shape.Setter == Setter.Accessor) is { Shape: { } hidden })
            {
                return (null, $"'{hidden.Name}' has a setter generated code cannot call; make it public or internal");
            }

            construction = $"new {EntityDiscovery.TypeName(type)} {{ {string.Join(", ", properties.Select((p, i) => p.Shape.Name + " = " + reads[i]))} }}";
        }
        else if (BindingConstructor(type, properties.Select(static p => p.Symbol).ToList(), compilation) is { } constructor)
        {
            // The constructor's parameters are properties; the other settable properties are set after it.
            var arguments = constructor.Parameters.Select(parameter => reads[properties.FindIndex(p => Matches(parameter, p.Symbol))]);
            var rest = properties
                .Select((p, i) => (p, i))
                .Where(x => !constructor.Parameters.Any(parameter => Matches(parameter, x.p.Symbol)))
                .ToList();
            if (rest.FirstOrDefault(static x => x.p.Shape.Setter == Setter.Accessor) is { p.Shape: { } hidden })
            {
                return (null, $"'{hidden.Name}' has a setter generated code cannot call; make it public or internal");
            }

            construction = $"new {EntityDiscovery.TypeName(type)}({string.Join(", ", arguments)})"
                + (rest.Count == 0 ? "" : $" {{ {string.Join(", ", rest.Select(x => x.p.Shape.Name + " = " + reads[x.i]))} }}");
        }
        else
        {
            return (null, "it needs an accessible parameterless constructor, or one whose parameters are its properties");
        }

        var projection = new ProjectionModel(
            properties.Select(static p => p.Shape.Name).ToEquatableArray(),
            default,
            $"static ({ProjectionTranslator.Reader}, {ProjectionTranslator.Ordinals}, {ProjectionTranslator.Entities}) => {construction}",
            default);
        var model = new SqlQueryModel(properties.Select(p => TypeOfName(p.Symbol.Type)).ToEquatableArray(), ReadsFirstColumn: false);
        var aliases = properties.Select(p => new Alias(p.Shape.Name, p.Shape.Name, type, p.Symbol)).ToList();
        return (new SqlQueryTranslation(projection, model, aliases), null);
    }

    // A scalar of a nullable type reads null; one of a value type throws EF Core's error on a null, as Nullable<T> does.
    private static SqlQueryTranslation Scalar(ITypeSymbol result, Compilation compilation)
    {
        var shape = EntityShapeBuilder.ScalarShape(result, ScalarColumn)!;
        var read = ProjectionTranslator.ReadColumn(shape, Ordinal(0), throughNavigation: true, asNullable: false);
        var projection = new ProjectionModel(
            new[] { ScalarColumn }.ToEquatableArray(),
            default,
            $"static ({ProjectionTranslator.Reader}, {ProjectionTranslator.Ordinals}, {ProjectionTranslator.Entities}) => {read}",
            default);
        var owner = result as INamedTypeSymbol ?? compilation.ObjectType;
        return new SqlQueryTranslation(
            projection,
            new SqlQueryModel(new[] { TypeOfName(result) }.ToEquatableArray(), ReadsFirstColumn: true),
            [new Alias("", ScalarColumn, owner, Property: null, ScalarType: result)]);
    }

    // A property of an unmapped type reads null only when its type allows it; a non-nullable one reads the column as it
    // is, so a null throws the provider's error, as in EF Core.
    private static string Read(PropertyShape shape, ITypeSymbol type, int index)
    {
        var canBeNull = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            || !type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated;
        return ProjectionTranslator.ReadColumn(shape with { CanBeNull = canBeNull }, Ordinal(index), throughNavigation: false, asNullable: false);
    }

    private static string Ordinal(int index) => $"{ProjectionTranslator.Ordinals}[{index.ToString(CultureInfo.InvariantCulture)}]";

    // typeof(...) takes no nullable reference annotation.
    private static string TypeOfName(ITypeSymbol type) => EntityDiscovery.TypeName(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
            {
                return property;
            }
        }

        return null;
    }

    // An accessible constructor whose parameters each match a property by name, ignoring case, and type, as a record's has.
    private static IMethodSymbol? BindingConstructor(INamedTypeSymbol type, List<IPropertySymbol> properties, Compilation compilation)
        => type.InstanceConstructors
            .Where(c => c.Parameters.Length > 0 && compilation.IsSymbolAccessibleWithin(c, compilation.Assembly))
            .Where(c => c.Parameters.All(parameter => properties.Any(property => Matches(parameter, property))))
            .OrderByDescending(static c => c.Parameters.Length)
            .FirstOrDefault();

    private static bool Matches(IParameterSymbol parameter, IPropertySymbol property)
        => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)
            && SymbolEqualityComparer.Default.Equals(parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
}

/// <summary>A translated <c>Database.SqlQuery</c> result: its reader, its columns, and what the result's members stand for.</summary>
internal readonly record struct SqlQueryTranslation(ProjectionModel Projection, SqlQueryModel Model, IReadOnlyList<Alias> Aliases);
