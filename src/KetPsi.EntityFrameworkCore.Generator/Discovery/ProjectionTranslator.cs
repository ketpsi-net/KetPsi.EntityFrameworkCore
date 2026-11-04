using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates a <c>Select</c> lambda into the property paths and entities it reads, and into a reader that rebuilds
/// the lambda's result from a row: the same construction, with each property read from its column and each entity
/// taken from the ones the runtime read. Anonymous types are rebuilt with the same members in the same order, which
/// makes them the same type within the assembly.
/// </summary>
internal sealed class ProjectionTranslator
{
    public const string Reader = "reader";
    public const string Ordinals = "ordinals";
    public const string Entities = "entities";

    private const string ReasonComputed =
        "only entity properties, navigations and entities can be projected, combined with new, records, " +
        "object initializers and ValueTuple.Create; computed values are not supported yet";
    private const string ReasonCollection = "collection navigations cannot be projected yet; Include them on a projected entity instead";
    private const string ReasonInitializer = "only property and field assignments are supported in object initializers";
    private const string NullableObjectMustHaveAValue = "Nullable object must have a value.";

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly INamedTypeSymbol _root;
    private readonly LocationInfo _location;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _propertyPaths = [];
    private readonly List<string> _entityPaths = [];
    private readonly Dictionary<string, EntityShape> _shapes = new(StringComparer.Ordinal);
    private readonly List<EntityShape> _entities = [];

    private ProjectionTranslator(
        SemanticModel model, IParameterSymbol parameter, INamedTypeSymbol root, LocationInfo location, CancellationToken cancellationToken)
    {
        _model = model;
        _parameter = parameter;
        _root = root;
        _location = location;
        _cancellationToken = cancellationToken;
    }

    private Compilation Compilation => _model.Compilation;

    /// <summary>Translates <paramref name="lambda"/>, returning either the projection or why it cannot be translated.</summary>
    /// <param name="lambda">The selector.</param>
    /// <param name="root">The queried entity, the lambda's parameter type.</param>
    /// <param name="model">The semantic model of the lambda's tree.</param>
    /// <param name="location">Where diagnostics about the entities point when they have no source.</param>
    /// <param name="cancellationToken">Cancels the translation.</param>
    public static (ProjectionModel? Projection, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda, INamedTypeSymbol root, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var parameterSyntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        if (parameterSyntax is null
            || model.GetDeclaredSymbol(parameterSyntax, cancellationToken) is not IParameterSymbol parameter
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (null, Unsupported(lambda, ReasonComputed));
        }

        var translator = new ProjectionTranslator(model, parameter, root, location, cancellationToken);
        try
        {
            var code = translator.Translate(operation);

            // A lambda whose body converts to the result type, such as x => x.Id for an object result.
            var typeInfo = model.GetTypeInfo(body, cancellationToken);
            if (typeInfo.ConvertedType is { IsAnonymousType: false } converted && !SymbolEqualityComparer.Default.Equals(converted, typeInfo.Type))
            {
                code = $"({translator.NameOf(converted, body)})({code})";
            }

            return (
                new ProjectionModel(
                    translator._propertyPaths.ToEquatableArray(),
                    translator._entityPaths.ToEquatableArray(),
                    $"static ({Reader}, {Ordinals}, {Entities}) => {code}",
                    translator._entities.ToEquatableArray()),
                null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    private string Translate(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (operation)
        {
            case IConversionOperation conversion when IsNullableOf(conversion.Type, conversion.Operand.Type) && MemberPath(conversion.Operand) is { } path:
                // (int?)x.Blog.Id is null when there is no blog, as in EF Core, rather than an error.
                return Member(path, conversion.Operand.Syntax, asNullable: true);

            case IConversionOperation conversion:
                return $"(({NameOf(conversion.Type!, conversion.Syntax)})({Translate(conversion.Operand)}))";

            case IParenthesizedOperation parenthesized:
                return Translate(parenthesized.Operand);

            case IAnonymousObjectCreationOperation anonymous:
                return "new { " + string.Join(", ", anonymous.Initializers.Select(i => Assignment(i, anonymous.Syntax))) + " }";

            case IObjectCreationOperation creation:
                return Creation(creation);

            case IInvocationOperation { TargetMethod: { IsStatic: true, Name: "Create" } method } invocation
                when method.ContainingType.ToDisplayString() == "System.ValueTuple":
                return $"global::System.ValueTuple.Create({Arguments(invocation.Arguments)})";

            case IParameterReferenceOperation or IPropertyReferenceOperation when MemberPath(operation) is { } path:
                return Member(path, operation.Syntax, asNullable: false);

            default:
                throw Fail(operation.Syntax, ReasonComputed);
        }
    }

    private string Creation(IObjectCreationOperation creation)
    {
        if (creation.Type is not INamedTypeSymbol type || creation.Constructor is not { } constructor || !IsAccessible(constructor))
        {
            throw Fail(creation.Syntax, "the constructor must be accessible to generated code in the same assembly");
        }

        var code = $"new {NameOf(type, creation.Syntax)}({Arguments(creation.Arguments)})";
        if (creation.Initializer is not { Initializers.Length: > 0 } initializer)
        {
            return code;
        }

        return code + " { " + string.Join(", ", initializer.Initializers.Select(i => Assignment(i, creation.Syntax))) + " }";
    }

    // One member of an object or anonymous type initializer: Name = value.
    private string Assignment(IOperation initializer, SyntaxNode owner)
    {
        if (initializer is not ISimpleAssignmentOperation { Target: IMemberReferenceOperation target } assignment)
        {
            throw Fail(initializer.Syntax, ReasonInitializer);
        }

        var settable = target.Member switch
        {
            _ when target.Member.ContainingType.IsAnonymousType => true,
            IPropertySymbol { SetMethod: { } setter } => IsAccessible(setter),
            IFieldSymbol field => IsAccessible(field),
            _ => false,
        };
        if (!settable)
        {
            throw Fail(owner, $"'{target.Member.Name}' must be settable by generated code in the same assembly");
        }

        return $"{target.Member.Name} = {Translate(assignment.Value)}";
    }

    private string Arguments(IEnumerable<IArgumentOperation> arguments)
        => string.Join(", ", arguments.Select(a => Translate(a.Value)));

    /// <summary>
    /// The properties a member access reads from the lambda's parameter, such as <c>x.Blog.Name</c>; an empty path
    /// for the parameter itself; <see langword="null"/> for anything else.
    /// </summary>
    private List<IPropertySymbol>? MemberPath(IOperation operation)
    {
        var properties = new List<IPropertySymbol>();
        while (operation is IPropertyReferenceOperation { Instance: { } instance, Property: var property })
        {
            properties.Insert(0, property);
            operation = instance;
        }

        return operation is IParameterReferenceOperation { Parameter: var parameter } && SymbolEqualityComparer.Default.Equals(parameter, _parameter)
            ? properties
            : null;
    }

    // A path through reference navigations to a column, which is read, or to an entity, which the runtime reads.
    private string Member(List<IPropertySymbol> path, SyntaxNode syntax, bool asNullable)
    {
        var entity = _root;
        for (var i = 0; i < path.Count; i++)
        {
            var property = path[i];
            var isLast = i == path.Count - 1;
            if (isLast && EntityShapeBuilder.IsReadableScalar(property.Type))
            {
                return Column(entity, property, path, syntax, asNullable);
            }

            if (EntityShapeBuilder.IsCollectionNavigation(property.Type))
            {
                throw Fail(syntax, ReasonCollection);
            }

            entity = EntityShapeBuilder.NavigationTarget(property.Type)
                ?? throw Fail(syntax, $"'{property.Name}' is neither a column nor a navigation that KetPsi.EntityFrameworkCore can read");
        }

        return Entity(entity, path, syntax);
    }

    private string Column(INamedTypeSymbol entity, IPropertySymbol property, List<IPropertySymbol> path, SyntaxNode syntax, bool asNullable)
    {
        var shape = Shape(entity).Properties.FirstOrDefault(p => p.Name == property.Name)
            ?? throw Fail(syntax, $"'{property.Name}' is not a mapped property: it has no setter, or it is [NotMapped]");

        var ordinal = $"{Ordinals}[{_propertyPaths.Count.ToString(CultureInfo.InvariantCulture)}]";
        _propertyPaths.Add(string.Join(".", path.Select(static p => p.Name)));

        var call = shape.Read == ReadMethod.GetFieldValue
            ? $"{Reader}.GetFieldValue<{shape.ValueTypeName}>({ordinal})"
            : $"{Reader}.{shape.Read}({ordinal})";
        var isNullableValue = shape.TypeName.EndsWith("?", StringComparison.Ordinal);
        var valueType = isNullableValue ? shape.TypeName.Substring(0, shape.TypeName.Length - 1) : shape.TypeName;

        // Enums are read as their underlying number.
        if (shape.Read != ReadMethod.GetFieldValue && shape.ValueTypeName is not null)
        {
            call = $"({valueType}){call}";
        }

        if (asNullable)
        {
            return $"({Reader}.IsDBNull({ordinal}) ? ({valueType}?)null : ({valueType}?){call})";
        }

        if (shape.CanBeNull)
        {
            // A non-nullable reference property can still hold a database null, as it does in EF Core.
            return isNullableValue
                ? $"({Reader}.IsDBNull({ordinal}) ? ({valueType}?)null : {call})"
                : $"({Reader}.IsDBNull({ordinal}) ? null! : {call})";
        }

        // A value read through an optional navigation is null when there is no related entity, which EF Core reports.
        return path.Count > 1
            ? $"({Reader}.IsDBNull({ordinal}) ? throw new global::System.InvalidOperationException(\"{NullableObjectMustHaveAValue}\") : {call})"
            : call;
    }

    private string Entity(INamedTypeSymbol entity, List<IPropertySymbol> path, SyntaxNode syntax)
    {
        var index = _entityPaths.Count;
        _entityPaths.Add(string.Join(".", path.Select(static p => p.Name)));

        var shape = Shape(entity);
        if (!_entities.Contains(shape))
        {
            _entities.Add(shape);
        }

        return $"(({NameOf(entity, syntax)}){Entities}[{index.ToString(CultureInfo.InvariantCulture)}]!)";
    }

    private EntityShape Shape(INamedTypeSymbol entity)
    {
        var name = EntityDiscovery.TypeName(entity);
        if (!_shapes.TryGetValue(name, out var shape))
        {
            _shapes.Add(name, shape = EntityShapeBuilder.Build(entity, Compilation, _location));
        }

        return shape;
    }

    // Types the reader names must be accessible to the generated code, which lives in the same assembly.
    private string NameOf(ITypeSymbol type, SyntaxNode syntax)
        => IsAccessible(type)
            ? EntityDiscovery.TypeName(type)
            : throw Fail(syntax, $"the type '{type.ToDisplayString()}' must be accessible to generated code in the same assembly");

    private bool IsAccessible(ISymbol symbol) => Compilation.IsSymbolAccessibleWithin(symbol, Compilation.Assembly);

    private static bool IsNullableOf(ITypeSymbol? type, ITypeSymbol? underlying)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], underlying);

    private static TranslationException Fail(SyntaxNode syntax, string reason) => new(Unsupported(syntax, reason));

    private static DiagnosticInfo Unsupported(SyntaxNode syntax, string reason)
        => DiagnosticInfo.Create(DiagnosticDescriptors.ProjectionNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);
}
