using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates a <c>Select</c> lambda into the property paths and entities it reads, and into a reader that rebuilds
/// the lambda's result from a row: the same construction, with each property read from its column and each entity
/// taken from the ones the runtime read. Anonymous types are rebuilt with the same members in the same order, which
/// makes them the same type within the assembly.
/// </summary>
/// <remarks>
/// Any other expression is computed in C# on the values read, as EF Core evaluates a final projection on the client.
/// Its code is the lambda's own, made independent of where it was written: names are fully qualified, extension methods
/// are called as static methods, constants are written out, and each captured value is read from <c>captures</c>, which
/// the interceptor fills from the expression tree in source order.
/// </remarks>
internal sealed partial class ProjectionTranslator
{
    public const string Reader = "reader";
    public const string Ordinals = "ordinals";
    public const string Entities = "entities";
    public const string Captures = "captures";

    private const string ReasonLambda = "the selector must be a lambda with one parameter and an expression body";
    private const string ReasonJoinedNavigation =
        "navigations of an entity joined by Join cannot be projected yet; select its columns or the entity itself";
    private const string ReasonInstanceMethod =
        "methods of the enclosing instance cannot be called from generated code; compute the value before the query";
    private const string ReasonThis = "the enclosing instance itself cannot be projected; capture the values it holds instead";
    private const string ReasonCollection = "collection navigations cannot be projected yet; Include them on a projected entity instead";
    private const string ReasonInitializer = "only property and field assignments are supported in object initializers";
    private static readonly SymbolDisplayFormat AnnotatedFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly Dictionary<IParameterSymbol, IReadOnlyList<Alias>> _parameters = new(SymbolEqualityComparer.Default);
    private readonly LocationInfo _location;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _propertyPaths = [];
    private readonly List<string> _entityPaths = [];
    private readonly Dictionary<string, EntityShape> _shapes = new(StringComparer.Ordinal);
    private readonly List<EntityShape> _entities = [];

    // Captured values are numbered in source order, which is the order the runtime reads them from the tree, whatever
    // order they are translated in: each is written with a placeholder that ResolveCaptures numbers.
    private readonly SortedSet<int> _captureSpans = [];
    private bool _hasComputedValues;

    private ProjectionTranslator(
        SemanticModel model, IParameterSymbol parameter, INamedTypeSymbol root, LocationInfo location, CancellationToken cancellationToken)
        : this(model, [parameter], [Alias.At("", root)], location, cancellationToken)
    {
    }

    private ProjectionTranslator(
        SemanticModel model,
        IReadOnlyList<IParameterSymbol> parameters,
        IReadOnlyList<IReadOnlyList<Alias>> aliases,
        LocationInfo location,
        CancellationToken cancellationToken)
    {
        _model = model;
        _parameter = parameters[0];
        for (var i = 0; i < parameters.Count; i++)
        {
            _parameters[parameters[i]] = aliases[i];
        }

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
        var (projection, _, diagnostic) = Translate(lambda, [Alias.At("", root)], model, location, cancellationToken);
        return (projection, diagnostic);
    }

    /// <summary>
    /// Translates a selector whose parameters stand for <paramref name="parameters"/>, one alias list per parameter,
    /// such as a <c>Join</c> result selector or a <c>Select</c> after a projection the operators read through. Also returns
    /// what the projection's members stand for, for the operators after it.
    /// </summary>
    public static (ProjectionModel? Projection, IReadOnlyList<Alias>? Aliases, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda,
        IReadOnlyList<IReadOnlyList<Alias>> parameters,
        SemanticModel model,
        LocationInfo location,
        CancellationToken cancellationToken)
    {
        var parameterSyntaxes = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToList(),
            _ => [],
        };

        var symbols = parameterSyntaxes.Select(s => model.GetDeclaredSymbol(s, cancellationToken)).OfType<IParameterSymbol>().ToList();
        if (symbols.Count == 0 || symbols.Count != parameters.Count
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (null, null, Unsupported(lambda, ReasonLambda));
        }

        var translator = new ProjectionTranslator(model, symbols, parameters, location, cancellationToken);
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
                    translator.ReaderLambda(code),
                    translator._entities.ToEquatableArray(),
                    translator._hasComputedValues,
                    translator.CaptureCount),
                translator.AliasesOf(operation, memberPath: ""),
                null);
        }
        catch (TranslationException exception)
        {
            return (null, null, exception.Diagnostic);
        }
    }

    /// <summary>
    /// What the members of a projected value stand for: the value itself when it is a column or an entity, and the
    /// members of an anonymous type or an object initializer that are, by member path. Computed members stand for nothing.
    /// </summary>
    private List<Alias> AliasesOf(IOperation value, string memberPath)
    {
        while (value is IConversionOperation { IsImplicit: true } conversion)
        {
            value = conversion.Operand;
        }

        if (value is IParameterReferenceOperation { Parameter: var parameter } && _parameters.TryGetValue(parameter, out var aliases))
        {
            return [.. aliases.Select(a => a with { MemberPath = Alias.Combine(memberPath, [a.MemberPath]) })];
        }

        // A member of a parameter that stands for several members, such as the pair of an earlier join.
        if (AliasedGroup(value) is { } group)
        {
            return [.. group.Aliases.Select(a => a with { MemberPath = Alias.Combine(memberPath, [a.MemberPath.Substring(group.Prefix.Length).TrimStart('.')]) })];
        }

        if (value is IPropertyReferenceOperation && MemberPath(value) is { } path && IsReadable(path, value.Syntax))
        {
            if (path.IsColumn)
            {
                return [new Alias(memberPath, path.Prefix, path.Owner, path.Column, path.ScalarType)];
            }

            var owner = path.Owner;
            for (var i = 0; i < path.Rest.Count; i++)
            {
                if (EntityShapeBuilder.IsReadableScalar(path.Rest[i].Type))
                {
                    return i == path.Rest.Count - 1 ? [new Alias(memberPath, path.Path, owner, path.Rest[i])] : [];
                }

                owner = EntityShapeBuilder.NavigationTarget(path.Rest[i].Type)!;
            }

            return [new Alias(memberPath, path.Path, owner)];
        }

        var initializers = value switch
        {
            IAnonymousObjectCreationOperation anonymous => anonymous.Initializers,
            IObjectCreationOperation { Initializer: { } initializer } => initializer.Initializers,
            _ => [],
        };

        var members = new List<Alias>();
        foreach (var initializer in initializers)
        {
            if (initializer is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.Name: var name }, Value: var member })
            {
                members.AddRange(AliasesOf(member, Alias.Combine(memberPath, [name])));
            }
        }

        return members;
    }

    /// <summary>
    /// The property path a selector reads, such as <c>"Price"</c> for <c>x =&gt; x.Price</c> or <c>"Blog.Name"</c> for
    /// <c>x =&gt; x.Blog.Name</c>; <see langword="null"/> for anything else.
    /// </summary>
    public static string? TranslatePropertyPath(LambdaExpressionSyntax lambda, INamedTypeSymbol root, SemanticModel model, CancellationToken cancellationToken)
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
            return null;
        }

        var translator = new ProjectionTranslator(model, parameter, root, LocationInfo.From(lambda.GetLocation()), cancellationToken);
        try
        {
            return translator.MemberPath(operation) is { Column: null, Rest.Count: > 0 } path
                && IsReadable(path, body)
                && EntityShapeBuilder.IsReadableScalar(path.Rest[path.Rest.Count - 1].Type)
                    ? path.Path
                    : null;
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    private int CaptureCount => _captureSpans.Count;

    // A reader that uses captured values closes over them; one that doesn't is static.
    private string ReaderLambda(string body)
        => $"{(CaptureCount == 0 ? "static " : "")}({Reader}, {Ordinals}, {Entities}) => {ResolveCaptures(body)}";

    /// <summary>Replaces each capture placeholder in <paramref name="code"/> with its index in source order.</summary>
    private string ResolveCaptures(string code)
    {
        var index = 0;
        foreach (var span in _captureSpans)
        {
            code = code.Replace(CapturePlaceholder(span), index.ToString(CultureInfo.InvariantCulture));
            index++;
        }

        return code;
    }

    private static string CapturePlaceholder(int spanStart) => "__kpcapture" + spanStart.ToString(CultureInfo.InvariantCulture) + "_";

    private string Translate(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (_group is not null && TryGroupLeaf(operation, out var leaf, out _))
        {
            return leaf;
        }

        switch (operation)
        {
            case IConversionOperation conversion when IsNullableOf(conversion.Type, conversion.Operand.Type)
                && MemberPath(conversion.Operand) is { } path && IsReadable(path, conversion.Operand.Syntax):
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

            case IParameterReferenceOperation or IPropertyReferenceOperation
                when MemberPath(operation) is { } path && IsReadable(path, operation.Syntax):
                return Member(path, operation.Syntax, asNullable: false);

            case IParameterReferenceOperation or IPropertyReferenceOperation when AliasedGroup(operation) is { } group:
                return Rebuild(group.Type, group.Aliases, group.Prefix, operation.Syntax);

            default:
                return Computed(operation.Syntax);
        }
    }

    // An expression computed in C# from the columns, entities and captured values it uses.
    private string Computed(SyntaxNode syntax)
    {
        _hasComputedValues = true;
        var rewritten = new ComputedWriter(this).Visit(syntax);
        return "(" + rewritten!.NormalizeWhitespace().ToFullString() + ")";
    }

    /// <summary>
    /// Whether a member path is read directly: a column or an entity, through reference navigations. A path that goes
    /// on past a column, such as <c>x.Created.Year</c>, is computed from the column instead.
    /// </summary>
    /// <exception cref="TranslationException">The path goes through a collection navigation, or a navigation of a joined entity.</exception>
    private static bool IsReadable(AliasedPath path, SyntaxNode syntax)
    {
        if (path.IsColumn)
        {
            return true;
        }

        var rest = path.Rest;
        for (var i = 0; i < rest.Count; i++)
        {
            var type = rest[i].Type;
            if (EntityShapeBuilder.IsCollectionNavigation(type))
            {
                throw Fail(syntax, ReasonCollection);
            }

            var isLast = i == rest.Count - 1;
            if (EntityShapeBuilder.NavigationTarget(type) is null)
            {
                return isLast && EntityShapeBuilder.IsReadableScalar(type);
            }

            // A joined entity is joined before any paging, which its navigations would have to follow.
            if (path.IsThroughJoin)
            {
                throw Fail(syntax, ReasonJoinedNavigation);
            }
        }

        return true;
    }

    // The code that reads a captured value, numbered in source order once the translation is done.
    private string Capture(ExpressionSyntax syntax)
    {
        var type = _model.GetTypeInfo(syntax, _cancellationToken).Type ?? throw Fail(syntax, ReasonLambda);
        _captureSpans.Add(syntax.SpanStart);
        return $"(({NameOf(type, syntax)}){Captures}[{CapturePlaceholder(syntax.SpanStart)}]!)";
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

        var code = $"{target.Member.Name} = {Translate(assignment.Value)}";
        RecordGroupMember(target.Member.Name, assignment.Value);
        return code;
    }

    /// <summary>
    /// The members a parameter, or a member chain of one, stands for when it is a value of an anonymous type made of
    /// aliased members, such as the pair <c>x</c> of an earlier join; null for anything else.
    /// </summary>
    private (INamedTypeSymbol Type, IReadOnlyList<Alias> Aliases, string Prefix)? AliasedGroup(IOperation operation)
    {
        if (_group is not null || _readsNoMembers || operation.Type is not INamedTypeSymbol { IsAnonymousType: true } type)
        {
            return null;
        }

        var names = new List<string>();
        while (operation is IPropertyReferenceOperation { Instance: { } instance, Property: var property })
        {
            names.Insert(0, property.Name);
            operation = instance;
        }

        if (operation is not IParameterReferenceOperation { Parameter: var parameter } || !_parameters.TryGetValue(parameter, out var aliases))
        {
            return null;
        }

        var prefix = string.Join(".", names);
        var members = aliases.Where(a => prefix.Length == 0 || a.MemberPath.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList();
        return members.Count == 0 ? null : (type, members, prefix);
    }

    /// <summary>
    /// Rebuilds a value of an anonymous type from what its members stand for: the same anonymous type, since it has the
    /// same members in the same order in the same assembly. A member that stands for nothing, a computed one, cannot be.
    /// </summary>
    private string Rebuild(INamedTypeSymbol type, IReadOnlyList<Alias> aliases, string prefix, SyntaxNode syntax)
    {
        var members = new List<string>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            var path = Alias.Combine(prefix, [property.Name]);
            if (aliases.FirstOrDefault(a => a.MemberPath == path) is { } alias)
            {
                members.Add(property.Name + " = " + alias switch
                {
                    { Property: { } column } => Column(alias.Entity, column, alias.Path, syntax, asNullable: false),
                    { ScalarType: { } scalar } => ScalarColumn(scalar, alias.Path, syntax, asNullable: false),
                    _ => Entity(alias.Entity, alias.Path, syntax),
                });
            }
            else if (property.Type is INamedTypeSymbol { IsAnonymousType: true } nested && aliases.Any(a => a.MemberPath.StartsWith(path + ".", StringComparison.Ordinal)))
            {
                members.Add(property.Name + " = " + Rebuild(nested, aliases, path, syntax));
            }
            else
            {
                throw Fail(syntax, $"the member '{property.Name}' is computed, so it cannot be read back; select it again from the entities");
            }
        }

        return "new { " + string.Join(", ", members) + " }";
    }

    private string Arguments(IEnumerable<IArgumentOperation> arguments)
        => string.Join(", ", arguments.Select(a => Translate(a.Value)));

    /// <summary>
    /// The member chain a member access reads from a lambda parameter, such as <c>x.Blog.Name</c>, through what the
    /// parameter and its members stand for; <see langword="null"/> for anything else.
    /// </summary>
    private AliasedPath? MemberPath(IOperation operation)
    {
        // A group, or a query projected from groups, has no entity to read members of.
        if (_group is not null || _readsNoMembers)
        {
            return null;
        }

        return Alias.Resolve(operation, _parameters);
    }

    // A path through reference navigations to a column, which is read, or to an entity, which the runtime reads.
    private string Member(AliasedPath path, SyntaxNode syntax, bool asNullable)
    {
        if (path.Column is { } column)
        {
            return Column(path.Owner, column, path.Prefix, syntax, asNullable);
        }

        if (path.ScalarType is { } scalar)
        {
            return ScalarColumn(scalar, path.Prefix, syntax, asNullable);
        }

        var entity = path.Owner;
        var rest = path.Rest;
        for (var i = 0; i < rest.Count; i++)
        {
            var property = rest[i];
            var isLast = i == rest.Count - 1;
            if (isLast && EntityShapeBuilder.IsReadableScalar(property.Type))
            {
                return Column(entity, property, path.Path, syntax, asNullable);
            }

            if (EntityShapeBuilder.IsCollectionNavigation(property.Type))
            {
                throw Fail(syntax, ReasonCollection);
            }

            entity = EntityShapeBuilder.NavigationTarget(property.Type)
                ?? throw Fail(syntax, $"'{property.Name}' is neither a column nor a navigation that KetPsi.EntityFrameworkCore can read");
        }

        return Entity(entity, path.Path, syntax);
    }

    private string Column(INamedTypeSymbol entity, IPropertySymbol property, string path, SyntaxNode syntax, bool asNullable)
    {
        var shape = PropertyShape(entity, property, syntax);
        var ordinal = Ordinal(_propertyPaths.Count);
        _propertyPaths.Add(path);
        return ReadColumn(shape, ordinal, throughNavigation: ThroughNavigation(path), asNullable);
    }

    // The column of a scalar result of SqlQuery: a null in it throws for a value type, as Nullable<T> does in EF Core.
    private string ScalarColumn(ITypeSymbol type, string path, SyntaxNode syntax, bool asNullable)
    {
        var shape = EntityShapeBuilder.ScalarShape(type, path) ?? throw Fail(syntax, $"the type '{type.ToDisplayString()}' cannot be read");
        var ordinal = Ordinal(_propertyPaths.Count);
        _propertyPaths.Add(path);
        return ReadColumn(shape, ordinal, throughNavigation: true, asNullable);
    }

    // Whether a column path goes through a navigation, which can be missing; a joined entity always exists.
    private static bool ThroughNavigation(string path)
    {
        var segments = path.Split('.');
        var start = segments[0].Length > 0 && segments[0][0] == '$' ? 1 : 0;
        return segments.Length - start > 1;
    }

    private PropertyShape PropertyShape(INamedTypeSymbol entity, IPropertySymbol property, SyntaxNode syntax)
        => Shape(entity).Properties.FirstOrDefault(p => p.Name == property.Name)
            ?? throw Fail(syntax, $"'{property.Name}' is not a mapped property: it has no setter, or it is [NotMapped]");

    private static string Ordinal(int index) => $"{Ordinals}[{index.ToString(CultureInfo.InvariantCulture)}]";

    /// <summary>The code that reads a property's column at <paramref name="ordinal"/>, as the property's type needs it.</summary>
    /// <param name="shape">The property.</param>
    /// <param name="ordinal">The C# of the ordinal.</param>
    /// <param name="throughNavigation">Whether the column is read through a navigation, which can be missing.</param>
    /// <param name="asNullable">Whether the value is read as a nullable type, null for a database null.</param>
    internal static string ReadColumn(PropertyShape shape, string ordinal, bool throughNavigation, bool asNullable)
    {
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
        return throughNavigation
            ? $"({Reader}.IsDBNull({ordinal}) ? throw new global::System.InvalidOperationException(global::KetPsi.EntityFrameworkCore.Infrastructure.NullValue.Message) : {call})"
            : call;
    }

    private string Entity(INamedTypeSymbol entity, string path, SyntaxNode syntax)
    {
        var index = _entityPaths.Count;
        _entityPaths.Add(path);

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

    // Types the reader names must be accessible to the generated code, which lives in the same assembly. Nullable
    // annotations are kept, so a cast such as (string?) means the same to the generated code's nullable analysis.
    private string NameOf(ITypeSymbol type, SyntaxNode syntax)
        => IsAccessible(type)
            ? type.ToDisplayString(AnnotatedFormat)
            : throw Fail(syntax, $"the type '{type.ToDisplayString()}' must be accessible to generated code in the same assembly");

    private bool IsAccessible(ISymbol symbol) => Compilation.IsSymbolAccessibleWithin(symbol, Compilation.Assembly);

    // A constant written so it keeps its type: literals as they are, enums and narrow numbers with a cast.
    private string Constant(object? value, ITypeSymbol? type, SyntaxNode syntax)
    {
        var name = type is null ? null : NameOf(type, syntax);
        if (value is null)
        {
            return name is null ? "null" : $"(({name})null)";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum })
        {
            return $"(({name})({Number(value)}))";
        }

        return value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            bool b => b ? "true" : "false",
            int or long or ulong or uint or float or double or decimal => Number(value),
            _ => $"(({name})({Number(value)}))",
        };
    }

    private static string Number(object value) => value switch
    {
        float f when float.IsNaN(f) => "global::System.Single.NaN",
        float f when float.IsPositiveInfinity(f) => "global::System.Single.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "global::System.Single.NegativeInfinity",
        double d when double.IsNaN(d) => "global::System.Double.NaN",
        double d when double.IsPositiveInfinity(d) => "global::System.Double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "global::System.Double.NegativeInfinity",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong u => u.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u => u.ToString(CultureInfo.InvariantCulture) + "U",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static bool IsNullableOf(ITypeSymbol? type, ITypeSymbol? underlying)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && SymbolEqualityComparer.Default.Equals(nullable.TypeArguments[0], underlying);

    private static TranslationException Fail(SyntaxNode syntax, string reason) => new(Unsupported(syntax, reason));

    /// <summary>Rewrites a computed expression so it compiles in the generated code with the same meaning.</summary>
    private sealed class ComputedWriter(ProjectionTranslator translator) : CSharpSyntaxRewriter
    {
        private SemanticModel Model => translator._model;

        private CancellationToken CancellationToken => translator._cancellationToken;

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node is not ExpressionSyntax expression || IsMemberName(expression))
            {
                return base.Visit(node);
            }

            if (Replacement(expression) is { } code)
            {
                return SyntaxFactory.ParseExpression(code);
            }

            return base.Visit(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (Model.GetOperation(node, CancellationToken) is not IInvocationOperation { TargetMethod: var method })
            {
                return base.VisitInvocationExpression(node);
            }

            if (method.IsExtensionMethod && node.Expression is MemberAccessExpressionSyntax extension)
            {
                // Called as a static method, which needs no using directive for the extension's namespace.
                var receiver = Visit(extension.Expression)!.ToFullString();
                var arguments = node.ArgumentList.Arguments.Select(a => Visit(a.Expression)!.ToFullString());
                var reduced = method.ReducedFrom ?? method;
                var typeArguments = extension.Name is GenericNameSyntax generic ? Visit(generic.TypeArgumentList)!.ToFullString() : "";
                return SyntaxFactory.ParseExpression(
                    $"{translator.NameOf(reduced.ContainingType, node)}.{method.Name}{typeArguments}({string.Join(", ", new[] { receiver }.Concat(arguments))})");
            }

            if (!method.IsStatic && node.Expression is IdentifierNameSyntax or GenericNameSyntax)
            {
                throw Fail(node, ReasonInstanceMethod);
            }

            // A method of a string read from a nullable column returns null without it, as the SQL EF Core sends does.
            if (!method.IsStatic && method.ContainingType.SpecialType == SpecialType.System_String && !method.ReturnType.IsValueType
                && node.Expression is MemberAccessExpressionSyntax { Expression: var target, Name: var name } && IsColumn(target))
            {
                return SyntaxFactory.ParseExpression($"{Visit(target)!.ToFullString()}?.{name}{Visit(node.ArgumentList)!.ToFullString()}");
            }

            return base.VisitInvocationExpression(node);
        }

        // The C# that replaces the expression, or null to rewrite its parts.
        private string? Replacement(ExpressionSyntax expression)
        {
            var operation = Model.GetOperation(expression, CancellationToken);

            if (translator._group is not null && operation is not null)
            {
                if (translator.TryGroupLeaf(operation, out var leaf, out _))
                {
                    return leaf;
                }

                if (operation is IParameterReferenceOperation { Parameter: var parameter } && translator.IsGroupParameter(parameter))
                {
                    throw FailGroup(expression, ReasonGroupItself);
                }
            }

            // A column or entity of the lambda's parameter, read like any projected member.
            if (operation is IParameterReferenceOperation or IPropertyReferenceOperation
                && translator.MemberPath(operation) is { } path && IsReadable(path, expression))
            {
                return translator.Member(path, expression, asNullable: false);
            }

            if (operation is IParameterReferenceOperation or IPropertyReferenceOperation && translator.AliasedGroup(operation) is { } group)
            {
                return translator.Rebuild(group.Type, group.Aliases, group.Prefix, expression);
            }

            if (operation is IConversionOperation { IsImplicit: false } conversion && IsNullableOf(conversion.Type, conversion.Operand.Type)
                && translator.MemberPath(conversion.Operand) is { } nullablePath && IsReadable(nullablePath, expression))
            {
                return translator.Member(nullablePath, expression, asNullable: true);
            }

            if (expression is not LiteralExpressionSyntax && Model.GetConstantValue(expression, CancellationToken) is { HasValue: true } constant)
            {
                return translator.Constant(constant.Value, Model.GetTypeInfo(expression, CancellationToken).Type, expression);
            }

            if (expression is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                throw Fail(expression, ReasonThis);
            }

            var symbol = Model.GetSymbolInfo(expression, CancellationToken).Symbol;
            if (IsCaptured(expression, symbol))
            {
                return translator.Capture(expression);
            }

            return symbol switch
            {
                INamespaceSymbol @namespace when expression is NameSyntax or MemberAccessExpressionSyntax => "global::" + @namespace.ToDisplayString(),
                ITypeSymbol type when expression is NameSyntax or MemberAccessExpressionSyntax => translator.NameOf(type, expression),

                // A static method named without its type, from the same class or a using static directive.
                IMethodSymbol { IsStatic: true } method when expression is SimpleNameSyntax name && !IsMemberName(expression)
                    => translator.NameOf(method.ContainingType, expression) + "." + Visit(name)!.ToFullString(),
                _ => null,
            };
        }

        // Values the compiler captures in the tree: locals and parameters of the enclosing method, members of the
        // enclosing instance, and static fields and properties.
        private bool IsCaptured(ExpressionSyntax expression, ISymbol? symbol)
        {
            switch (symbol)
            {
                case ILocalSymbol:
                    return true;
                case IParameterSymbol parameter:
                    return !IsDeclaredIn(parameter, translator._parameter);
                case IFieldSymbol { IsConst: false } or IPropertySymbol when symbol.IsStatic:
                    return expression is SimpleNameSyntax or MemberAccessExpressionSyntax;
                case IFieldSymbol { IsConst: false } or IPropertySymbol:
                    return expression is SimpleNameSyntax
                        || expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax };
                default:
                    return false;
            }
        }

        // The selector's own parameter, or one of a lambda inside it, is not a captured value.
        private static bool IsDeclaredIn(IParameterSymbol parameter, IParameterSymbol selectorParameter)
        {
            if (SymbolEqualityComparer.Default.Equals(parameter, selectorParameter))
            {
                return true;
            }

            var selector = selectorParameter.ContainingSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            return selector is not null
                && parameter.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == selector.SyntaxTree && selector.Span.Contains(r.Span));
        }

        private bool IsColumn(ExpressionSyntax expression)
            => Model.GetOperation(expression, CancellationToken) is IPropertyReferenceOperation operation
                && translator.MemberPath(operation) is { } path
                && IsReadable(path, expression)
                && (path.IsColumn || path.Rest.Count > 0 && EntityShapeBuilder.IsReadableScalar(path.Rest[path.Rest.Count - 1].Type));

        // The name after the dot in a member access, which belongs to the expression before it.
        private static bool IsMemberName(ExpressionSyntax expression)
            => expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression
                || expression.Parent is MemberBindingExpressionSyntax;
    }

    private static DiagnosticInfo Unsupported(SyntaxNode syntax, string reason)
        => DiagnosticInfo.Create(DiagnosticDescriptors.ProjectionNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);
}
