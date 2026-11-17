using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Collections a projection selects, such as <c>x.Posts</c> or
/// <c>x.Posts.Where(p =&gt; p.IsPublic).OrderBy(p =&gt; p.Title).Select(p =&gt; p.Title).ToList()</c>: each becomes a
/// <c>CollectionProjection</c> the runtime loads with one more statement, by the keys of the rows' parents.
/// </summary>
/// <remarks>
/// The reader of a projection with collections reads the row into locals and returns what builds the result, which
/// the runtime calls once the collections are loaded; each collection's elements reach it as a <c>List&lt;T&gt;</c>
/// after the entities.
/// </remarks>
internal sealed partial class ProjectionTranslator
{
    public const string BuildHelper = "Build";
    public const string CollectionHelper = "Collection";
    public const string ItemsHelper = "Items";
    public const string CollectionLocal = "__kpcollection";

    private const string Infrastructure = "global::KetPsi.EntityFrameworkCore.Infrastructure";
    private const string Templates = Infrastructure + ".Templates";
    private const string ReasonCollectionShape =
        "a projected collection is the navigation itself, or Where, OrderBy, ThenBy and one Select on it, optionally followed by ToList() or ToArray()";
    private const string ReasonCollectionOuterRow =
        "the lambdas of a projected collection cannot read the projected row yet; use the collection's own elements";
    private const string ReasonCollectionOrder =
        "a projected collection is ordered by a property of its elements, or one through reference navigations";
    private const string ReasonCollectionType = "end the projected collection with ToList() or ToArray() to give it this type";
    private const string ReasonCollectionCreate =
        "the navigation's collection type cannot be created; declare it as List<T>, HashSet<T> or an interface they implement";

    // The construction of each collection the projection selects.
    private readonly List<string> _collections = [];

    // With collections: what the reader reads from the row, in order, which the result is built from later.
    private readonly List<string> _hoisted = [];
    private bool _deferred;

    // Whether this translates the Select of a projected collection's elements, which can't select collections.
    private bool _isElement;

    private static string CollectionIndexPlaceholder(int index) => "__kpcollectionindex" + index.ToString(CultureInfo.InvariantCulture) + "_";

    /// <summary>
    /// A collection navigation of the queried entity, with the operators on it, as a projection selects it; null for
    /// any other expression, and for one an operator after it continues.
    /// </summary>
    /// <exception cref="TranslationException">The collection's operators cannot be translated.</exception>
    private string? ProjectedCollection(IOperation operation)
    {
        if (!AllowsValues || operation.Syntax is not ExpressionSyntax expression || operation.Type is not { } type || IsContinued(expression))
        {
            return null;
        }

        // The calls on the navigation, first to last.
        var calls = new List<IInvocationOperation>();
        var source = operation;
        while (source is IInvocationOperation invocation && IsEnumerable(invocation.TargetMethod) && invocation.Arguments.Length > 0)
        {
            calls.Insert(0, invocation);
            source = Unwrapped(invocation.Arguments[0].Value);
        }

        if (source is not IPropertyReferenceOperation || MemberPath(source) is not { IsColumn: false, Rest.Count: > 0 } path
            || !EntityShapeBuilder.IsCollectionNavigation(path.Rest[path.Rest.Count - 1].Type))
        {
            return null;
        }

        for (var i = 0; i < path.Rest.Count - 1; i++)
        {
            if (EntityShapeBuilder.IsCollectionNavigation(path.Rest[i].Type) || EntityShapeBuilder.NavigationTarget(path.Rest[i].Type) is null)
            {
                return null;
            }
        }

        var element = EntityShapeBuilder.NavigationTarget(path.Rest[path.Rest.Count - 1].Type)!;
        var navigationPath = string.Join(".", path.Rest.Select(static p => p.Name));
        foreach (var call in calls)
        {
            foreach (var argument in call.Arguments.Skip(1))
            {
                if (argument.Syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                    .Any(name => SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(name, _cancellationToken).Symbol, _parameter)))
                {
                    throw Fail(argument.Syntax, ReasonCollectionOuterRow);
                }
            }
        }

        var slots = new List<string>();
        string? predicate = null;
        var orderings = new List<string>();
        LambdaExpressionSyntax? selector = null;
        ITypeSymbol? selected = null;
        var stage = 0;
        foreach (var call in calls)
        {
            var lambda = call.Arguments.Length == 2 ? LambdaOf(call.Arguments[1].Value) : null;
            switch (call.TargetMethod.Name)
            {
                case "Where" when stage == 0 && lambda is not null && IsElementLambda(call):
                    var condition = PredicateTranslator.TranslateElementPredicate(lambda, _model, slots, _cancellationToken);
                    predicate = predicate is null ? condition : $"new {Templates}.AndTemplate({predicate}, {condition})";
                    break;
                case "OrderBy" or "OrderByDescending" when stage == 0 && lambda is not null:
                    orderings.Clear();
                    orderings.Add(Ordering(lambda, element, call.TargetMethod.Name == "OrderByDescending"));
                    break;
                case "ThenBy" or "ThenByDescending" when stage == 0 && lambda is not null && orderings.Count > 0:
                    orderings.Add(Ordering(lambda, element, call.TargetMethod.Name == "ThenByDescending"));
                    break;
                case "Select" when stage == 0 && lambda is not null && IsElementLambda(call):
                    (selector, selected, stage) = (lambda, call.TargetMethod.TypeArguments[1], 1);
                    break;
                case "ToList" or "ToArray" when stage < 2 && call.Arguments.Length == 1:
                    stage = 2;
                    break;
                default:
                    throw Fail(call.Syntax, ReasonCollectionShape);
            }
        }

        ClaimSlotCaptures(slots, firstSlot: 0);
        var (propertyNames, entityPaths, reader) = selector is null ? EntityElement(element, expression) : ProjectedElement(selector, element, selected!);

        var index = _collections.Count;
        _collections.Add(
            $"{CollectionHelper}(\"{navigationPath}\", {predicate ?? "null"}, {ObjectArray(slots)}, " +
            $"{(orderings.Count == 0 ? $"global::System.Array.Empty<{Infrastructure}.CollectionOrdering>()" : $"new {Infrastructure}.CollectionOrdering[] {{ {string.Join(", ", orderings)} }}")}, " +
            $"{StringArray(propertyNames)}, {StringArray(entityPaths)}, {reader})");

        var items = Hoist($"{ItemsHelper}({Entities}[{CollectionIndexPlaceholder(index)}], {CollectionLocal}{index.ToString(CultureInfo.InvariantCulture)})");
        return Converted(items, type, calls.Count == 0 ? element : null, expression);
    }

    // Whether the expression is the source of a further call of Enumerable, such as x.Posts in x.Posts.Skip(1).
    private bool IsContinued(ExpressionSyntax expression)
        => expression.Parent is MemberAccessExpressionSyntax access && access.Expression == expression
            && access.Parent is InvocationExpressionSyntax invocation
            && _model.GetSymbolInfo(invocation, _cancellationToken).Symbol is IMethodSymbol method
            && IsEnumerable(method);

    private static bool IsEnumerable(IMethodSymbol method)
        => (method.ReducedFrom ?? method).ContainingType.ToDisplayString() == "System.Linq.Enumerable";

    private static IOperation Unwrapped(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static LambdaExpressionSyntax? LambdaOf(IOperation argument)
        => Unwrapped(argument) is IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Syntax: LambdaExpressionSyntax lambda } } ? lambda : null;

    // Whether the call takes a lambda over the element alone, not the overload that also takes its index.
    private static bool IsElementLambda(IInvocationOperation call)
        => call.TargetMethod.Parameters[call.TargetMethod.Parameters.Length - 1].Type is INamedTypeSymbol { TypeArguments.Length: 2 };

    private string Ordering(LambdaExpressionSyntax lambda, INamedTypeSymbol element, bool descending)
    {
        var path = TranslatePropertyPath(lambda, element, _model, _cancellationToken) ?? throw Fail(lambda, ReasonCollectionOrder);
        return $"new {Infrastructure}.CollectionOrdering(\"{path}\", {(descending ? "true" : "false")})";
    }

    // The related entity itself, which the runtime reads with its tracking and identity resolution.
    private (List<string> PropertyNames, List<string> EntityPaths, string Reader) EntityElement(INamedTypeSymbol element, SyntaxNode syntax)
    {
        AddShape(Shape(element));
        return ([], [""], $"static ({Reader}, {Ordinals}, {Entities}) => (({NameOf(element, syntax)}){Entities}[0]!)");
    }

    // The Select of the collection, translated over the related entity like a projection of its own.
    private (List<string> PropertyNames, List<string> EntityPaths, string Reader) ProjectedElement(
        LambdaExpressionSyntax selector, INamedTypeSymbol element, ITypeSymbol selected)
    {
        var parameterSyntax = selector switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        if (parameterSyntax is null || _model.GetDeclaredSymbol(parameterSyntax, _cancellationToken) is not IParameterSymbol parameter
            || selector.Body is not ExpressionSyntax body || _model.GetOperation(body, _cancellationToken) is not { } operation)
        {
            throw Fail(selector, ReasonLambda);
        }

        var translator = new ProjectionTranslator(_model, parameter, element, _location, _cancellationToken) { _isElement = true };
        var code = translator.Translate(operation);
        if (!selected.IsAnonymousType && !SymbolEqualityComparer.Default.Equals(selected, operation.Type))
        {
            code = $"(({NameOf(selected, body)})({code}))";
        }

        // Captured values are numbered with the projection's own, in source order.
        _captureSpans.UnionWith(translator._captureSpans);
        foreach (var shape in translator._entities)
        {
            AddShape(shape);
        }

        var reader = $"{(translator.CaptureCount == 0 ? "static " : "")}({Reader}, {Ordinals}, {Entities}) => {code}";
        return (translator._propertyPaths, translator._entityPaths, reader);
    }

    private void AddShape(EntityShape shape)
    {
        if (!_entities.Contains(shape))
        {
            _entities.Add(shape);
        }
    }

    /// <summary>
    /// The elements as the expression's type: the list itself, an array, a set the navigation's type needs, or the list
    /// as an interface it implements.
    /// </summary>
    /// <param name="items">The C# of the elements' <c>List&lt;T&gt;</c>.</param>
    /// <param name="type">The expression's type.</param>
    /// <param name="navigationElement">For the navigation itself, its element type; null after operators.</param>
    /// <param name="syntax">Where a diagnostic points.</param>
    private string Converted(string items, ITypeSymbol type, INamedTypeSymbol? navigationElement, SyntaxNode syntax)
    {
        if (type is IArrayTypeSymbol)
        {
            return $"global::System.Linq.Enumerable.ToArray({items})";
        }

        if (type is not INamedTypeSymbol named || ElementOf(named) is not { } elementType)
        {
            throw Fail(syntax, ReasonCollectionType);
        }

        var list = Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!.Construct(elementType);
        if (SymbolEqualityComparer.Default.Equals(named, list))
        {
            return items;
        }

        // A type with an anonymous element can't be named: it is the list, or the list as a sequence.
        if (ContainsAnonymous(named))
        {
            return named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                ? $"global::System.Linq.Enumerable.AsEnumerable({items})"
                : throw Fail(syntax, ReasonCollectionType);
        }

        // The navigation itself is the collection EF Core creates for it: a set when its type can hold one.
        if (navigationElement is not null)
        {
            var set = Compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1")!.Construct(elementType);
            if (SymbolEqualityComparer.Default.Equals(named, set) || named.TypeKind == TypeKind.Interface && IsImplicit(set, named))
            {
                return $"(({NameOf(named, syntax)})new global::System.Collections.Generic.HashSet<{NameOf(elementType, syntax)}>({items}))";
            }

            if (named.TypeKind != TypeKind.Interface)
            {
                throw Fail(syntax, ReasonCollectionCreate);
            }
        }

        return IsImplicit(list, named) ? $"(({NameOf(named, syntax)}){items})" : throw Fail(syntax, ReasonCollectionType);
    }

    private bool IsImplicit(ITypeSymbol from, ITypeSymbol to) => Compilation.ClassifyCommonConversion(from, to).IsImplicit;

    private static ITypeSymbol? ElementOf(INamedTypeSymbol type)
    {
        var enumerable = type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            ? type
            : type.AllInterfaces.FirstOrDefault(static i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable?.TypeArguments[0];
    }

    private static bool ContainsAnonymous(ITypeSymbol type) => type switch
    {
        { IsAnonymousType: true } => true,
        IArrayTypeSymbol array => ContainsAnonymous(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsAnonymous),
        _ => false,
    };

    // The variables the slots capture are numbered with the projection's own, in source order.
    private void ClaimSlotCaptures(List<string> slots, int firstSlot)
    {
        for (var i = firstSlot; i < slots.Count; i++)
        {
            slots[i] = SlotCapture.Replace(slots[i], match =>
            {
                var span = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                _captureSpans.Add(span);
                return CapturePlaceholder(span);
            });
        }
    }

    private static string ObjectArray(List<string> values)
        => values.Count == 0 ? "global::System.Array.Empty<object?>()" : $"new object?[] {{ {string.Join(", ", values)} }}";

    private static string StringArray(List<string> values)
        => values.Count == 0
            ? "global::System.Array.Empty<string>()"
            : "new string[] { " + string.Join(", ", values.Select(static v => "\"" + v + "\"")) + " }";
}
