using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates <c>GroupBy</c> and the lambdas over its groups. A group is read through the values the statement
/// computes for it, each a <c>GroupValue</c>: a part of the key, or an aggregate of the group's rows. A <c>Select</c>
/// on groups reads value <c>i</c> at ordinal <c>i</c>, and computes anything else in C# from them, as projections do.
/// </summary>
internal sealed partial class ProjectionTranslator
{
    private const string GroupValueType = "global::KetPsi.EntityFrameworkCore.Infrastructure.GroupValue";
    private const string AggregateFunctionType = "global::KetPsi.EntityFrameworkCore.Sql.AggregateFunction";
    private const string AggregateValueTypeType = "global::KetPsi.EntityFrameworkCore.Sql.AggregateValueType";
    private const string GroupingType = "System.Linq.IGrouping<TKey, TElement>";

    private const string ReasonKey =
        "a key must be a property, one through reference navigations, or an anonymous type of them; " +
        "EF Core groups other keys through a derived table, which KetPsi.EntityFrameworkCore does not support yet";
    private const string ReasonTupleKey = "EF Core cannot translate a ValueTuple.Create key; use an anonymous type, such as x => new { x.A, x.B }";
    private const string ReasonGroupItself =
        "the group itself cannot be read; select its key and aggregates, such as g.Key and g.Count()";
    private const string ReasonGroupOperator =
        "only Count, LongCount, Sum, Average, Min and Max of a group, Count with a predicate or after Where, and " +
        "Select(x => x.Property).Distinct().Count() can be translated; EF Core reads other uses of a group, such as " +
        "g.First() or g.ToList(), with joins that KetPsi.EntityFrameworkCore does not support yet";
    private const string ReasonGroupSelector =
        "an aggregate of a group must read one property of the element, such as x => x.Price";
    private const string ReasonGroupNavigation =
        "an aggregate through a navigation is a correlated subquery in EF Core, which KetPsi.EntityFrameworkCore does not support yet";
    private const string ReasonGroupElementSelector =
        "an aggregate without a selector needs an element selector that reads one property, such as GroupBy(x => x.Key, x => x.Price)";
    private const string ReasonGroupPredicate = "a count predicate reads the grouped entity, so GroupBy cannot have an element selector";
    private const string ReasonGroupCorrelated = "a predicate over the group's rows can only compare them with values, not with the group's key or aggregates";
    private const string ReasonGroupSelect = "a Select on groups must read the key or an aggregate";
    private const string ReasonGroupOrder = "an ordering of groups must read the key, a part of it, or an aggregate";
    private const string ReasonCompositeKey = "a composite key is compared one part at a time, such as g.Key.Status";
    private const string ReasonProjectedMember =
        "only members that are a key part or an aggregate as they are, without a predicate, can be read after Select; " +
        "filter and order the groups before the Select instead";

    private readonly List<string> _groupValues = [];
    private readonly Dictionary<string, int> _groupValueIndexes = new(StringComparer.Ordinal);
    private readonly List<string> _predicateValues = [];
    private readonly List<GroupMember> _groupMembers = [];
    private GroupShape? _group;
    private IParameterSymbol? _groupParameter;
    private IParameterSymbol? _keyParameter;
    private EquatableArray<GroupMember> _projectedMembers;
    private bool _readsNoMembers;
    private IOperation? _lastLeaf;
    private string? _lastLeafValue;
    private int _lastValueIndex;

    /// <summary>
    /// Translates a <c>GroupBy</c> call: its key, its element and, for the overloads with one, its result selector.
    /// </summary>
    /// <param name="invocation">The <c>GroupBy</c> call.</param>
    /// <param name="entity">The grouped entity.</param>
    /// <param name="model">The semantic model of the call's tree.</param>
    /// <param name="location">Where diagnostics about the entities point when they have no source.</param>
    /// <param name="cancellationToken">Cancels the translation.</param>
    public static (GroupShape? Group, DiagnosticInfo? Diagnostic) TranslateGroupBy(
        IInvocationOperation invocation, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var lambdas = invocation.Arguments
            .Where(static a => a.Parameter?.Ordinal > 0)
            .OrderBy(static a => a.Parameter!.Ordinal)
            .Select(static a => (Lambda: Lambda(a.Value), Arity: FuncArity(a.Parameter!.Type)))
            .ToList();
        if (lambdas.Count == 0 || lambdas.Any(static l => l.Lambda is null))
        {
            return (null, null);
        }

        var keyLambda = lambdas[0].Lambda!;
        var elementLambda = lambdas.Count > 1 && lambdas[1].Arity == 2 ? lambdas[1].Lambda : null;
        var resultLambda = lambdas[lambdas.Count - 1].Arity == 3 ? lambdas[lambdas.Count - 1].Lambda : null;

        var (key, keyDiagnostic) = TranslateKey(keyLambda, entity, model, location, cancellationToken);
        if (keyDiagnostic is not null)
        {
            return (null, keyDiagnostic);
        }

        var (keyParts, keyKind, keyProjection) = key!.Value;
        GroupElement element;
        ProjectionModel elementProjection;
        if (elementLambda is null)
        {
            element = new GroupElement(IsEntity: true);
            elementProjection = new ProjectionModel(
                default,
                new[] { "" }.ToEquatableArray(),
                $"static ({Reader}, {Ordinals}, {Entities}) => (({EntityDiscovery.TypeName(entity)}){Entities}[0]!)",
                new[] { EntityShapeBuilder.Build(entity, model.Compilation, location) }.ToEquatableArray());
        }
        else
        {
            var (projection, elementDiagnostic) = Translate(elementLambda, entity, model, location, cancellationToken);
            if (elementDiagnostic is not null)
            {
                return (null, elementDiagnostic);
            }

            elementProjection = projection!;
            element = ElementOf(elementLambda, entity, model, cancellationToken);
        }

        var groupBy = new GroupByModel(
            EntityDiscovery.TypeName(entity),
            keyParts.Select(static p => string.Join(".", p.Path.Select(static s => s.Name))).ToEquatableArray(),
            keyProjection.ReaderCode,
            elementProjection.PropertyPaths,
            elementProjection.EntityPaths,
            elementProjection.ReaderCode,
            elementProjection.CaptureCount,
            elementProjection.Entities);
        var group = new GroupShape(entity, keyParts, keyKind, element, groupBy);
        if (resultLambda is null)
        {
            return (group, null);
        }

        var (result, resultDiagnostic) = TranslateGroupSelect(resultLambda, group, model, location, cancellationToken);
        if (resultDiagnostic is not null)
        {
            return (null, resultDiagnostic);
        }

        return (group.WithResult(SelectCall(result!, GroupCallKind.Select), result!), null);
    }

    /// <summary>
    /// Translates a <c>Select</c> on groups, <c>g =&gt; ...</c>, or a result selector, <c>(key, g) =&gt; ...</c>, into
    /// the values it reads and a reader over them.
    /// </summary>
    public static (ProjectionModel? Projection, DiagnosticInfo? Diagnostic) TranslateGroupSelect(
        LambdaExpressionSyntax lambda, GroupShape group, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (ForGroup(lambda, group, model, location, cancellationToken) is not { } translator
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (null, Unsupported(lambda, ReasonLambda));
        }

        try
        {
            var code = translator.Translate(operation);
            translator.RecordGroupMember("", operation);
            if (translator._groupValues.Count == 0)
            {
                throw FailGroup(body, ReasonGroupSelect);
            }

            var typeInfo = model.GetTypeInfo(body, cancellationToken);
            if (typeInfo.ConvertedType is { IsAnonymousType: false } converted && !SymbolEqualityComparer.Default.Equals(converted, typeInfo.Type))
            {
                code = $"({translator.NameOf(converted, body)})({code})";
            }

            return (
                new ProjectionModel(
                    default,
                    default,
                    translator.ReaderLambda(code),
                    default,
                    translator._hasComputedValues,
                    translator.CaptureCount,
                    translator.Resolved(translator._groupValues),
                    translator.Resolved(translator._predicateValues),
                    translator._groupMembers.ToEquatableArray()),
                null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    /// <summary>Translates a predicate on groups, <c>g =&gt; g.Count() &gt; 1</c>, into a <c>HAVING</c> template over group values.</summary>
    public static (GroupCallModel? Call, DiagnosticInfo? Diagnostic) TranslateGroupPredicate(
        LambdaExpressionSyntax lambda, GroupShape group, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (ForGroup(lambda, group, model, location, cancellationToken) is not { } translator)
        {
            return (null, Unsupported(lambda, ReasonLambda));
        }

        return translator.TranslatePredicate(lambda, translator.GroupOperand);
    }

    /// <summary>Translates the key selector of an ordering on groups, which reads one group value.</summary>
    public static (GroupCallModel? Call, DiagnosticInfo? Diagnostic) TranslateGroupOrder(
        LambdaExpressionSyntax lambda, GroupShape group, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (ForGroup(lambda, group, model, location, cancellationToken) is not { } translator
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (null, Unsupported(lambda, ReasonLambda));
        }

        try
        {
            if (translator.IsWholeKey(operation) && group.KeyKind != GroupKeyKind.Single)
            {
                throw FailGroup(body, ReasonCompositeKey);
            }

            if (!translator.TryGroupLeaf(operation, out _, out _))
            {
                throw FailGroup(body, ReasonGroupOrder);
            }

            return (translator.Call(GroupCallKind.Order, templateCode: null), null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    /// <summary>
    /// Translates a predicate after a <c>Select</c> on groups, over the members that are group values, into a
    /// <c>HAVING</c> template.
    /// </summary>
    public static (GroupCallModel? Call, DiagnosticInfo? Diagnostic) TranslateProjectedPredicate(
        LambdaExpressionSyntax lambda, ProjectionModel projection, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (ForProjected(lambda, projection, entity, model, location, cancellationToken) is not { } translator)
        {
            return (null, Unsupported(lambda, ReasonLambda));
        }

        return translator.TranslatePredicate(lambda, translator.ProjectedOperand);
    }

    /// <summary>Translates the key selector of an ordering after a <c>Select</c> on groups, which reads one member that is a group value.</summary>
    public static (GroupCallModel? Call, DiagnosticInfo? Diagnostic) TranslateProjectedOrder(
        LambdaExpressionSyntax lambda, ProjectionModel projection, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (ForProjected(lambda, projection, entity, model, location, cancellationToken) is not { } translator
            || lambda.Body is not ExpressionSyntax body)
        {
            return (null, Unsupported(lambda, ReasonLambda));
        }

        try
        {
            _ = translator.ProjectedOperand(body);
            return (translator.Call(GroupCallKind.ProjectedOrder, templateCode: null), null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    // A Select or result selector on groups, as an interceptor passes it.
    private static GroupCallModel SelectCall(ProjectionModel projection, GroupCallKind kind)
        => new(kind, Values: projection.GroupValues, PredicateValues: projection.PredicateValues, ReaderCode: projection.ReaderCode, CaptureCount: projection.CaptureCount);

    /// <summary>The model of a <c>Select</c> on groups, as an interceptor passes it.</summary>
    public static GroupCallModel SelectCall(ProjectionModel projection) => SelectCall(projection, GroupCallKind.Select);

    private (GroupCallModel? Call, DiagnosticInfo? Diagnostic) TranslatePredicate(LambdaExpressionSyntax lambda, Func<ExpressionSyntax, PredicateOperand?> operands)
    {
        var (predicate, diagnostic) = PredicateTranslator.Translate(lambda, _model, _cancellationToken, ValueCode, operands, _predicateValues);
        if (diagnostic is not null)
        {
            return (null, diagnostic);
        }

        var kind = _group is null ? GroupCallKind.ProjectedWhere : GroupCallKind.Where;
        return (Call(kind, predicate!.TemplateCode), null);
    }

    private GroupCallModel Call(GroupCallKind kind, string? templateCode)
        => new(kind, Values: Resolved(_groupValues), PredicateValues: Resolved(_predicateValues), TemplateCode: templateCode, CaptureCount: CaptureCount);

    private EquatableArray<string> Resolved(List<string> codes) => codes.Select(ResolveCaptures).ToEquatableArray();

    private static ProjectionTranslator? ForGroup(
        LambdaExpressionSyntax lambda, GroupShape group, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var parameters = Parameters(lambda, model, cancellationToken);
        if (parameters is not ({ Count: 1 } or { Count: 2 }))
        {
            return null;
        }

        return new ProjectionTranslator(model, parameters[0], group.Entity, location, cancellationToken)
        {
            _group = group,
            _keyParameter = parameters.Count == 2 ? parameters[0] : null,
            _groupParameter = parameters[parameters.Count - 1],
        };
    }

    private static ProjectionTranslator? ForProjected(
        LambdaExpressionSyntax lambda, ProjectionModel projection, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
        => Parameters(lambda, model, cancellationToken) is { Count: 1 } parameters
            ? new ProjectionTranslator(model, parameters[0], entity, location, cancellationToken)
            {
                _readsNoMembers = true,
                _projectedMembers = projection.GroupMembers,
            }
            : null;

    private static List<IParameterSymbol>? Parameters(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
    {
        IEnumerable<ParameterSyntax> syntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            _ => [],
        };

        var parameters = new List<IParameterSymbol>();
        foreach (var parameter in syntax)
        {
            if (model.GetDeclaredSymbol(parameter, cancellationToken) is not IParameterSymbol symbol)
            {
                return null;
            }

            parameters.Add(symbol);
        }

        return parameters;
    }

    private static (( IReadOnlyList<KeyPart> Parts, GroupKeyKind Kind, ProjectionModel Projection)? Key, DiagnosticInfo? Diagnostic) TranslateKey(
        LambdaExpressionSyntax lambda, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var node = lambda.Body as SyntaxNode ?? lambda;
        if (Parameters(lambda, model, cancellationToken) is not { Count: 1 } parameters
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (null, KeyNotSupported(node));
        }

        var parameter = parameters[0];
        var parts = new List<KeyPart>();
        GroupKeyKind kind;
        switch (operation)
        {
            case IAnonymousObjectCreationOperation anonymous:
                kind = GroupKeyKind.Anonymous;
                foreach (var initializer in anonymous.Initializers)
                {
                    if (initializer is not ISimpleAssignmentOperation { Target: IPropertyReferenceOperation target, Value: var value }
                        || PathOf(value, parameter, entity) is not { } path)
                    {
                        return (null, KeyNotSupported(initializer.Syntax));
                    }

                    parts.Add(new KeyPart(target.Property.Name, path.Path, path.Owner));
                }

                break;

            case IInvocationOperation { TargetMethod: { IsStatic: true, Name: "Create" } method }
                when method.ContainingType.ToDisplayString() == "System.ValueTuple":
                // EF Core cannot translate a tuple key, so it is not one KetPsi translates either.
                return (null, KeyNotSupported(node, ReasonTupleKey));

            default:
                if (PathOf(operation, parameter, entity) is not { } single)
                {
                    return (null, KeyNotSupported(node));
                }

                kind = GroupKeyKind.Single;
                parts.Add(new KeyPart(null, single.Path, single.Owner));
                break;
        }

        if (parts.Count == 0)
        {
            return (null, KeyNotSupported(node));
        }

        var (projection, diagnostic) = Translate(lambda, entity, model, location, cancellationToken);
        if (diagnostic is not null)
        {
            return (null, diagnostic);
        }

        // The key of groups returned directly: EF Core cannot read a null into a nullable value-type key there, and
        // throws, so the reader does too. A Select on the groups reads the key itself, null included.
        var translator = new ProjectionTranslator(model, parameter, entity, location, cancellationToken);
        var reads = parts.Select((part, index) => translator.ReadGroupedKey(part, index, node)).ToList();
        var code = kind == GroupKeyKind.Single
            ? reads[0]
            : "new { " + string.Join(", ", parts.Select((part, index) => $"{part.MemberName} = {reads[index]}")) + " }";
        return ((parts, kind, projection! with { ReaderCode = translator.ReaderLambda(code) }), null);
    }

    private string ReadGroupedKey(KeyPart part, int index, SyntaxNode syntax)
    {
        var shape = PropertyShape(part.Owner, part.Path[part.Path.Count - 1], syntax);
        var ordinal = Ordinal(index);
        var read = ReadColumn(shape, ordinal, throughNavigation: part.Path.Count > 1, asNullable: false);
        return shape.TypeName.EndsWith("?", StringComparison.Ordinal)
            ? $"({Reader}.IsDBNull({ordinal}) ? throw new global::System.InvalidOperationException(global::KetPsi.EntityFrameworkCore.Infrastructure.NullValue.Message) : {read})"
            : read;
    }

    private static DiagnosticInfo KeyNotSupported(SyntaxNode syntax, string reason = ReasonKey)
        => DiagnosticInfo.Create(DiagnosticDescriptors.GroupKeyNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);

    /// <summary>
    /// The properties a member access reads from <paramref name="parameter"/> to a column, through reference navigations,
    /// and the entity that declares the column; <see langword="null"/> for anything else.
    /// </summary>
    private static (IReadOnlyList<IPropertySymbol> Path, INamedTypeSymbol Owner)? PathOf(IOperation operation, IParameterSymbol parameter, INamedTypeSymbol entity)
    {
        var path = new List<IPropertySymbol>();
        while (operation is IPropertyReferenceOperation { Instance: { } instance, Property: var property })
        {
            path.Insert(0, property);
            operation = instance;
        }

        if (path.Count == 0
            || operation is not IParameterReferenceOperation { Parameter: var reference }
            || !SymbolEqualityComparer.Default.Equals(reference, parameter))
        {
            return null;
        }

        var owner = entity;
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (EntityShapeBuilder.IsCollectionNavigation(path[i].Type) || EntityShapeBuilder.NavigationTarget(path[i].Type) is not { } target)
            {
                return null;
            }

            owner = target;
        }

        return EntityShapeBuilder.IsReadableScalar(path[path.Count - 1].Type) ? (path, owner) : null;
    }

    // What an element selector's elements are, for the aggregates that read them.
    private static GroupElement ElementOf(LambdaExpressionSyntax lambda, INamedTypeSymbol entity, SemanticModel model, CancellationToken cancellationToken)
    {
        if (Parameters(lambda, model, cancellationToken) is not { Count: 1 } parameters
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return new GroupElement(IsEntity: false);
        }

        if (PathOf(operation, parameters[0], entity) is { } single)
        {
            return new GroupElement(IsEntity: false, Path: single.Path);
        }

        if (operation is IAnonymousObjectCreationOperation anonymous)
        {
            var members = new Dictionary<string, IReadOnlyList<IPropertySymbol>>(StringComparer.Ordinal);
            foreach (var initializer in anonymous.Initializers)
            {
                if (initializer is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation target, Value: var value }
                    && PathOf(value, parameters[0], entity) is { } path)
                {
                    members[target.Property.Name] = path.Path;
                }
            }

            return new GroupElement(IsEntity: false, Members: members);
        }

        return new GroupElement(IsEntity: false);
    }

    private bool IsGroupParameter(IParameterSymbol parameter)
        => SymbolEqualityComparer.Default.Equals(parameter, _groupParameter) || SymbolEqualityComparer.Default.Equals(parameter, _keyParameter);

    /// <summary>
    /// Reads a group value when <paramref name="operation"/> is one: the key, a part of it, or an aggregate of the group.
    /// </summary>
    /// <param name="operation">The expression.</param>
    /// <param name="code">The C# that reads the value from the row.</param>
    /// <param name="valueCode">
    /// The C# of its <c>GroupValue</c>, when another call can read it: not for a count with a predicate, whose value
    /// slots belong to this call, nor for a whole composite key.
    /// </param>
    private bool TryGroupLeaf(IOperation operation, out string code, out string? valueCode)
    {
        valueCode = null;
        if (IsWholeKey(operation))
        {
            code = WholeKey(operation.Syntax, out valueCode);
        }
        else if (KeyPartIndex(operation) is { } part)
        {
            code = ReadKeyPart(part, operation.Syntax, out valueCode);
        }
        else if (operation is IInvocationOperation invocation && IsGroupRooted(SourceOf(invocation)))
        {
            code = ReadAggregate(invocation, out valueCode);
        }
        else
        {
            code = "";
            return false;
        }

        _lastLeaf = operation;
        _lastLeafValue = valueCode;
        return true;
    }

    // A member that is a group value as it is can be read by a Where or an ordering after the Select.
    private void RecordGroupMember(string name, IOperation value)
    {
        if (_group is not null && ReferenceEquals(_lastLeaf, value) && _lastLeafValue is { } code)
        {
            _groupMembers.Add(new GroupMember(name, ResolveCaptures(code)));
        }
    }

    private bool IsWholeKey(IOperation operation) => operation switch
    {
        IParameterReferenceOperation { Parameter: var parameter } => _keyParameter is not null && SymbolEqualityComparer.Default.Equals(parameter, _keyParameter),
        IPropertyReferenceOperation { Property: { Name: "Key" } property, Instance: IParameterReferenceOperation { Parameter: var parameter } }
            => SymbolEqualityComparer.Default.Equals(parameter, _groupParameter)
                && property.ContainingType.OriginalDefinition.ToDisplayString() == GroupingType,
        _ => false,
    };

    private int? KeyPartIndex(IOperation operation)
    {
        var (instance, name) = operation is IPropertyReferenceOperation { Instance: { } target, Property.Name: var member } ? (target, member) : (null, null);

        if (instance is null || _group!.KeyKind == GroupKeyKind.Single || !IsWholeKey(instance))
        {
            return null;
        }

        for (var i = 0; i < _group.KeyParts.Count; i++)
        {
            if (_group.KeyParts[i].MemberName == name)
            {
                return i;
            }
        }

        return null;
    }

    private string WholeKey(SyntaxNode syntax, out string? valueCode)
    {
        var group = _group!;
        if (group.KeyKind == GroupKeyKind.Single)
        {
            return ReadKeyPart(0, syntax, out valueCode);
        }

        valueCode = null;
        var parts = group.KeyParts.Select((p, i) => (p.MemberName, Code: ReadKeyPart(i, syntax, out _))).ToList();
        return "new { " + string.Join(", ", parts.Select(static p => $"{p.MemberName} = {p.Code}")) + " }";
    }

    private string ReadKeyPart(int index, SyntaxNode syntax, out string? valueCode)
    {
        var part = _group!.KeyParts[index];
        valueCode = $"{GroupValueType}.Key({index.ToString(CultureInfo.InvariantCulture)})";
        var ordinal = Ordinal(Register(valueCode, hasPredicate: false));
        var shape = PropertyShape(part.Owner, part.Path[part.Path.Count - 1], syntax);
        return ReadColumn(shape, ordinal, throughNavigation: part.Path.Count > 1, asNullable: false);
    }

    // The values a lambda reads are selected once each, except counts with a predicate, whose value slots differ.
    private int Register(string valueCode, bool hasPredicate)
    {
        if (hasPredicate || !_groupValueIndexes.TryGetValue(valueCode, out var index))
        {
            index = _groupValues.Count;
            _groupValues.Add(valueCode);
            if (!hasPredicate)
            {
                _groupValueIndexes.Add(valueCode, index);
            }
        }

        _lastValueIndex = index;
        return index;
    }

    private static IOperation? SourceOf(IInvocationOperation invocation)
        => invocation.TargetMethod.ContainingType.ToDisplayString() == KnownTypes.Enumerable
            ? Strip(invocation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value)
            : null;

    private static IOperation? Strip(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    // Whether a sequence is the group itself, or an Enumerable operator applied to it.
    private bool IsGroupRooted(IOperation? operation) => Strip(operation) switch
    {
        IParameterReferenceOperation { Parameter: var parameter } => SymbolEqualityComparer.Default.Equals(parameter, _groupParameter),
        IInvocationOperation invocation => IsGroupRooted(SourceOf(invocation)),
        _ => false,
    };

    private bool IsGroup(IOperation? operation)
        => Strip(operation) is IParameterReferenceOperation { Parameter: var parameter } && SymbolEqualityComparer.Default.Equals(parameter, _groupParameter);

    // The Enumerable call on the group that the aggregate applies to, such as Where in g.Where(p).Count().
    private static IInvocationOperation? Inner(IInvocationOperation invocation, string name, int arguments)
        => Strip(SourceOf(invocation)) is IInvocationOperation { TargetMethod.Name: var innerName } inner
            && innerName == name && inner.Arguments.Length == arguments
            ? inner
            : null;

    private string ReadAggregate(IInvocationOperation invocation, out string? valueCode)
    {
        var method = invocation.TargetMethod;
        var syntax = invocation.Syntax;
        var arguments = invocation.Arguments.OrderBy(static a => a.Parameter?.Ordinal).ToList();
        var source = SourceOf(invocation);
        valueCode = null;
        switch (method.Name)
        {
            case "Count" or "LongCount":
                var isLong = method.Name == "LongCount" ? "true" : "false";
                string? predicate = null;
                if (arguments.Count == 2 && IsGroup(source))
                {
                    predicate = CountPredicate(arguments[1], syntax);
                }
                else if (arguments.Count == 1 && Inner(invocation, "Where", 2) is { } where && IsGroup(SourceOf(where)))
                {
                    predicate = CountPredicate(where.Arguments.First(static a => a.Parameter?.Ordinal == 1), syntax);
                }
                else if (arguments.Count == 1 && Inner(invocation, "Distinct", 1) is { } distinct
                    && Inner(distinct, "Select", 2) is { } select && IsGroup(SourceOf(select)))
                {
                    var (distinctProperty, _) = ElementProperty(Lambda(select.Arguments.First(static a => a.Parameter?.Ordinal == 1).Value), syntax);
                    var distinctCode = $"{GroupValueType}.DistinctCount(\"{distinctProperty.Name}\", isLong: {isLong})";
                    valueCode = distinctCode;
                    return Count(distinctCode, hasPredicate: false, isLong);
                }
                else if (arguments.Count != 1 || !IsGroup(source))
                {
                    throw FailGroup(syntax, ReasonGroupOperator);
                }

                var countCode = predicate is null
                    ? $"{GroupValueType}.Count(isLong: {isLong})"
                    : $"{GroupValueType}.Count(isLong: {isLong}, {predicate})";
                valueCode = predicate is null ? countCode : null;
                return Count(countCode, predicate is not null, isLong);

            case "Sum" or "Average" or "Min" or "Max":
                LambdaExpressionSyntax? selector;
                ITypeSymbol selected;
                if (arguments.Count == 2 && IsGroup(source))
                {
                    selector = Lambda(arguments[1].Value);
                    selected = ((INamedTypeSymbol)method.Parameters[1].Type).TypeArguments[1];
                }
                else if (arguments.Count == 1 && IsGroup(source))
                {
                    selector = null;
                    selected = ((INamedTypeSymbol)method.Parameters[0].Type).TypeArguments[0];
                }
                else if (arguments.Count == 1 && Inner(invocation, "Select", 2) is { } select && IsGroup(SourceOf(select)))
                {
                    selector = Lambda(select.Arguments.First(static a => a.Parameter?.Ordinal == 1).Value);
                    selected = ((INamedTypeSymbol)method.Parameters[0].Type).TypeArguments[0];
                }
                else
                {
                    throw FailGroup(syntax, ReasonGroupOperator);
                }

                var (property, owner) = ElementProperty(selector, syntax);
                var kind = method.Name switch
                {
                    "Sum" => OperatorKind.Sum,
                    "Average" => OperatorKind.Average,
                    "Min" => OperatorKind.Min,
                    _ => OperatorKind.Max,
                };
                var aggregateCode =
                    $"{GroupValueType}.Aggregate({AggregateFunctionType}.{method.Name}, \"{property.Name}\", {AggregateValueTypeType}.{CallSiteAnalyzer.AggregateValueType(kind, selected)})";
                valueCode = aggregateCode;
                var ordinal = Ordinal(Register(aggregateCode, hasPredicate: false));
                return kind is OperatorKind.Min or OperatorKind.Max
                    ? ReadColumn(PropertyShape(owner, property, syntax), ordinal, throughNavigation: false, asNullable: false)
                    : ReadNumber(method.ReturnType, ordinal, syntax);

            default:
                throw FailGroup(syntax, ReasonGroupOperator);
        }
    }

    private string Count(string valueCode, bool hasPredicate, string isLong)
    {
        var ordinal = Ordinal(Register(valueCode, hasPredicate));
        return isLong == "true" ? $"{Reader}.GetInt64({ordinal})" : $"{Reader}.GetInt32({ordinal})";
    }

    // Sum and Average read the type of their overload's result, nullable when it is.
    private string ReadNumber(ITypeSymbol type, string ordinal, SyntaxNode syntax)
    {
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
        var read = underlying.SpecialType switch
        {
            SpecialType.System_Int32 => "GetInt32",
            SpecialType.System_Int64 => "GetInt64",
            SpecialType.System_Decimal => "GetDecimal",
            SpecialType.System_Double => "GetDouble",
            SpecialType.System_Single => "GetFloat",
            _ => throw FailGroup(syntax, ReasonGroupSelector),
        };

        var call = $"{Reader}.{read}({ordinal})";
        return ReferenceEquals(underlying, type)
            ? call
            : $"({Reader}.IsDBNull({ordinal}) ? ({NameOf(underlying, syntax)}?)null : {call})";
    }

    // The template of a count's predicate over the grouped entity, whose value slots are this call's.
    private string CountPredicate(IArgumentOperation argument, SyntaxNode syntax)
    {
        if (!_group!.Element.IsEntity)
        {
            throw FailGroup(syntax, ReasonGroupPredicate);
        }

        var lambda = Lambda(argument.Value) ?? throw FailGroup(syntax, ReasonGroupOperator);
        var (predicate, diagnostic) = PredicateTranslator.Translate(lambda, _model, _cancellationToken, ValueCode, operands: null, _predicateValues);
        return diagnostic is null ? predicate!.TemplateCode : throw new TranslationException(diagnostic);
    }

    // The property an aggregate of the group reads from its element, which must be a column of the entity itself.
    private (IPropertySymbol Property, INamedTypeSymbol Owner) ElementProperty(LambdaExpressionSyntax? selector, SyntaxNode syntax)
    {
        var element = _group!.Element;
        IReadOnlyList<IPropertySymbol>? path;
        if (selector is null)
        {
            path = element.Path ?? throw FailGroup(syntax, ReasonGroupElementSelector);
        }
        else if (Parameters(selector, _model, _cancellationToken) is not { Count: 1 } parameters
            || selector.Body is not ExpressionSyntax body
            || _model.GetOperation(body, _cancellationToken) is not { } operation)
        {
            throw FailGroup(selector, ReasonGroupSelector);
        }
        else if (element.IsEntity)
        {
            path = PathOf(operation, parameters[0], _group.Entity)?.Path ?? throw FailGroup(selector, NavigationOrSelector(operation));
        }
        else if (element.Members is { } members && operation is IPropertyReferenceOperation
            {
                Instance: IParameterReferenceOperation { Parameter: var parameter },
                Property.Name: var member,
            }
            && SymbolEqualityComparer.Default.Equals(parameter, parameters[0]) && members.TryGetValue(member, out var memberPath))
        {
            path = memberPath;
        }
        else if (element.Path is { } elementPath && operation is IParameterReferenceOperation { Parameter: var self }
            && SymbolEqualityComparer.Default.Equals(self, parameters[0]))
        {
            path = elementPath;
        }
        else
        {
            throw FailGroup(selector, ReasonGroupSelector);
        }

        return path.Count == 1 ? (path[0], _group.Entity) : throw FailGroup(selector ?? syntax, ReasonGroupNavigation);
    }

    // A selector through a navigation gets its own reason; anything else is not a property.
    private static string NavigationOrSelector(IOperation operation)
        => operation is IPropertyReferenceOperation { Instance: IPropertyReferenceOperation } ? ReasonGroupNavigation : ReasonGroupSelector;

    /// <summary>The C# of a value a predicate compares with: a constant or a captured value, read when the query runs.</summary>
    private string ValueCode(ExpressionSyntax value)
    {
        foreach (var identifier in value.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (_model.GetSymbolInfo(identifier, _cancellationToken).Symbol is IParameterSymbol parameter
                && (IsGroupParameter(parameter) || SymbolEqualityComparer.Default.Equals(parameter, _parameter)))
            {
                throw FailGroup(value, ReasonGroupCorrelated);
            }
        }

        var rewritten = new ComputedWriter(this).Visit(value);
        return rewritten!.NormalizeWhitespace().ToFullString();
    }

    // An operand of a predicate on groups: the key, a part of it, or an aggregate.
    private PredicateOperand? GroupOperand(ExpressionSyntax node)
    {
        if (_model.GetOperation(node, _cancellationToken) is not { } operation)
        {
            return null;
        }

        if (IsWholeKey(operation) && _group!.KeyKind != GroupKeyKind.Single)
        {
            throw FailGroup(node, ReasonCompositeKey);
        }

        return TryGroupLeaf(operation, out _, out _) ? Operand(node) : null;
    }

    // An operand of a predicate after a Select on groups: a member that is a group value, or the projection itself.
    private PredicateOperand? ProjectedOperand(ExpressionSyntax node)
    {
        var name = node switch
        {
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax identifier, Name.Identifier.ValueText: var memberName } when IsLambdaParameter(identifier) => memberName,
            IdentifierNameSyntax identifier when IsLambdaParameter(identifier) => "",
            _ => null,
        };

        var member = name is null ? null : _projectedMembers.FirstOrDefault(m => m.Name == name);
        if (member is null)
        {
            throw FailGroup(node, ReasonProjectedMember);
        }

        Register(member.ValueCode, hasPredicate: false);
        return Operand(node);
    }

    private bool IsLambdaParameter(IdentifierNameSyntax identifier)
        => SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(identifier, _cancellationToken).Symbol, _parameter);

    // The operand of the value just registered, named by its index, typed as the expression.
    private PredicateOperand Operand(ExpressionSyntax node)
        => new(
            _lastValueIndex.ToString(CultureInfo.InvariantCulture),
            _model.GetTypeInfo(node, _cancellationToken).Type ?? throw FailGroup(node, ReasonLambda));

    private static LambdaExpressionSyntax? Lambda(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation function } => function.Syntax as LambdaExpressionSyntax,
            IAnonymousFunctionOperation function => function.Syntax as LambdaExpressionSyntax,
            { Syntax: var syntax } => PredicateTranslator.StripParentheses((ExpressionSyntax)syntax) as LambdaExpressionSyntax,
            _ => null,
        };
    }

    // The number of type arguments of the Func an Expression<Func<...>> parameter takes.
    private static int FuncArity(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeArguments.Length: 1 } expression && expression.TypeArguments[0] is INamedTypeSymbol func
            ? func.TypeArguments.Length
            : 0;

    private static TranslationException FailGroup(SyntaxNode syntax, string reason)
        => new(DiagnosticInfo.Create(DiagnosticDescriptors.GroupExpressionNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason));
}
