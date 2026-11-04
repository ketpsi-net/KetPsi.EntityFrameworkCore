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
internal sealed class ProjectionTranslator
{
    public const string Reader = "reader";
    public const string Ordinals = "ordinals";
    public const string Entities = "entities";
    public const string Captures = "captures";

    private const string ReasonLambda = "the selector must be a lambda with one parameter and an expression body";
    private const string ReasonInstanceMethod =
        "methods of the enclosing instance cannot be called from generated code; compute the value before the query";
    private const string ReasonThis = "the enclosing instance itself cannot be projected; capture the values it holds instead";
    private const string ReasonCollection = "collection navigations cannot be projected yet; Include them on a projected entity instead";
    private const string ReasonInitializer = "only property and field assignments are supported in object initializers";
    private const string NullableObjectMustHaveAValue = "Nullable object must have a value.";

    private static readonly SymbolDisplayFormat AnnotatedFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly INamedTypeSymbol _root;
    private readonly LocationInfo _location;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _propertyPaths = [];
    private readonly List<string> _entityPaths = [];
    private readonly Dictionary<string, EntityShape> _shapes = new(StringComparer.Ordinal);
    private readonly List<EntityShape> _entities = [];
    private int _captureCount;
    private bool _hasComputedValues;

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
            return (null, Unsupported(lambda, ReasonLambda));
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

            // A reader that uses captured values closes over them; one that doesn't is static.
            var modifier = translator._captureCount == 0 ? "static " : "";
            return (
                new ProjectionModel(
                    translator._propertyPaths.ToEquatableArray(),
                    translator._entityPaths.ToEquatableArray(),
                    $"{modifier}({Reader}, {Ordinals}, {Entities}) => {code}",
                    translator._entities.ToEquatableArray(),
                    translator._hasComputedValues,
                    translator._captureCount),
                null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
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
            return translator.MemberPath(operation) is { Count: > 0 } path
                && translator.IsReadable(path, body)
                && EntityShapeBuilder.IsReadableScalar(path[path.Count - 1].Type)
                    ? string.Join(".", path.Select(static p => p.Name))
                    : null;
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    private string Translate(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

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
    /// <exception cref="TranslationException">The path goes through a collection navigation.</exception>
    private bool IsReadable(List<IPropertySymbol> path, SyntaxNode syntax)
    {
        for (var i = 0; i < path.Count; i++)
        {
            var type = path[i].Type;
            if (EntityShapeBuilder.IsCollectionNavigation(type))
            {
                throw Fail(syntax, ReasonCollection);
            }

            var isLast = i == path.Count - 1;
            if (EntityShapeBuilder.NavigationTarget(type) is null)
            {
                return isLast && EntityShapeBuilder.IsReadableScalar(type);
            }
        }

        return true;
    }

    // The code that reads a captured value, numbered in source order.
    private string Capture(ExpressionSyntax syntax)
    {
        var type = _model.GetTypeInfo(syntax, _cancellationToken).Type ?? throw Fail(syntax, ReasonLambda);
        var index = _captureCount++;
        return $"(({NameOf(type, syntax)}){Captures}[{index.ToString(CultureInfo.InvariantCulture)}]!)";
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

            // A column or entity of the lambda's parameter, read like any projected member.
            if (operation is IParameterReferenceOperation or IPropertyReferenceOperation
                && translator.MemberPath(operation) is { } path && translator.IsReadable(path, expression))
            {
                return translator.Member(path, expression, asNullable: false);
            }

            if (operation is IConversionOperation { IsImplicit: false } conversion && IsNullableOf(conversion.Type, conversion.Operand.Type)
                && translator.MemberPath(conversion.Operand) is { } nullablePath && translator.IsReadable(nullablePath, expression))
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
                && translator.MemberPath(operation) is { Count: > 0 } path
                && translator.IsReadable(path, expression)
                && EntityShapeBuilder.IsReadableScalar(path[path.Count - 1].Type);

        // The name after the dot in a member access, which belongs to the expression before it.
        private static bool IsMemberName(ExpressionSyntax expression)
            => expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression
                || expression.Parent is MemberBindingExpressionSyntax;
    }

    private static DiagnosticInfo Unsupported(SyntaxNode syntax, string reason)
        => DiagnosticInfo.Create(DiagnosticDescriptors.ProjectionNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);
}
