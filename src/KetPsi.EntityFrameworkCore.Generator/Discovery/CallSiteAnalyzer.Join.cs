using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

// Join, and the operators that read through a projection's members: the pair of a query-syntax join, or any
// projection whose members are entities or columns.
internal static partial class CallSiteAnalyzer
{
    private const string ReasonInner =
        "the inner sequence must be a DbSet of the same context or FromSql on one, optionally with Where, WhereIf, the tracking operators and IgnoreQueryFilters";
    private const string ReasonInnerEntity = "the inner sequence must be of an entity class";
    private const string ReasonKey =
        "a key must be a property of the entity, or one through reference navigations on the outer side, or an anonymous type of them";
    private const string ReasonInnerKey = "an inner key must be a property of the inner entity itself, or an anonymous type of them";
    private const string ReasonAliasAfterPaging = "filter, order, project and join before Skip, Take and Distinct";

    /// <summary>
    /// What the calls after <c>Database.SqlQuery</c> are projected from instead of an entity type: their sites are kept
    /// although no entity is involved.
    /// </summary>
    internal const string SqlQueryRoot = "<SqlQuery>";

    /// <summary>
    /// <c>Database.SqlQuery</c> or <c>SqlQueryRaw</c>: a query of a scalar or an unmapped type from SQL, read through its
    /// result's columns. A result type that cannot be read is KP1004.
    /// </summary>
    private static CallSite ClassifySqlQuery(
        IInvocationOperation operation, InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var method = operation.TargetMethod;
        var result = method.TypeArguments[0];
        var location = LocationInfo.From(((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.GetLocation());
        var (rows, reason) = SqlQueryTranslator.Translate(result, model.Compilation, location);
        if (rows is not { } translation)
        {
            return new CallSite(
                EntityDiscovery.TypeName(result),
                location,
                Operator: null,
                IsAsync: false,
                HasPredicate: false,
                DiagnosticInfo.Create(DiagnosticDescriptors.RawSqlNotSupported, location, result.ToDisplayString(), method.Name, reason!),
                ProjectedFrom: SqlQueryRoot);
        }

        var declared = method.ReducedFrom is null ? method : method.GetConstructedReducedFrom() ?? method;
        return new CallSite(
            EntityDiscovery.TypeName(result),
            location,
            method.Name == "SqlQuery" ? OperatorKind.SqlQuery : OperatorKind.SqlQueryRaw,
            IsAsync: false,
            HasPredicate: false,
            Diagnostic: null,
            Interception: model.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
            Signature: new InterceptorSignature(
                declared.Name,
                EntityDiscovery.TypeName(declared.ContainingType),
                declared.ReturnType.ToDisplayString(SignatureFormat),
                declared.Parameters.Select(static p => new ParameterSignature(p.Type.ToDisplayString(SignatureFormat), p.Name)).ToEquatableArray()),
            Projection: translation.Projection,
            ProjectedFrom: SqlQueryRoot,
            SqlQuery: translation.Model);
    }

    /// <summary>The operator a call on a projection is when it reads through the projection's members, or null.</summary>
    private static OperatorKind? AliasOperator(IMethodSymbol method)
    {
        var parameters = method.Parameters;
        return method.Name switch
        {
            "Where" when IsPredicate(parameters[1].Type) => OperatorKind.Where,
            "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" when parameters.Length == 2 => OrderKind(method.Name),
            "Select" when IsSelector(parameters[1].Type) => OperatorKind.Select,
            "Join" when parameters.Length == 5 => OperatorKind.Join,
            _ => null,
        };
    }

    /// <summary>
    /// The projection a <c>Join</c> makes: the joined entity is at the join path <c>"$"</c> followed by
    /// <paramref name="index"/>, and the result selector reads both sides through what they stand for.
    /// </summary>
    private static (JoinTranslation? Join, DiagnosticInfo? Diagnostic) TranslateJoin(
        IInvocationOperation join, IReadOnlyList<Alias> outer, int index, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var innerArgument = join.Arguments.First(static a => a.Parameter?.Ordinal == 1).Value;
        if (join.TargetMethod.TypeArguments[1] is not INamedTypeSymbol { TypeKind: TypeKind.Class } inner)
        {
            return (null, JoinDiagnostic(innerArgument.Syntax, ReasonInnerEntity));
        }

        if (!IsFilteredSet(innerArgument, model, cancellationToken))
        {
            return (null, JoinDiagnostic(innerArgument.Syntax, ReasonInner));
        }

        if (LambdaAt(join, 2) is not { } outerKey || LambdaAt(join, 3) is not { } innerKey || LambdaAt(join, 4) is not { } result)
        {
            return (null, null);
        }

        var innerAliases = Alias.At("", inner);
        if (KeyPaths(outerKey, outer, innerSide: false, model, cancellationToken) is not { } outerKeys)
        {
            return (null, JoinDiagnostic(outerKey.Body, ReasonKey));
        }

        if (KeyPaths(innerKey, innerAliases, innerSide: true, model, cancellationToken) is not { } innerKeys)
        {
            return (null, JoinDiagnostic(innerKey.Body, ReasonInnerKey));
        }

        var joined = Alias.At(JoinPathPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture), inner);
        var (projection, aliases, diagnostic) = ProjectionTranslator.Translate(result, [outer, joined], model, location, cancellationToken);
        if (projection is null)
        {
            return (null, diagnostic);
        }

        var keys = new JoinModel(outerKeys.ToEquatableArray(), innerKeys.ToEquatableArray(), EntityDiscovery.TypeName(inner));
        return (new JoinTranslation(keys, projection, aliases ?? []), null);
    }

    private const string JoinPathPrefix = "$";

    private static DiagnosticInfo JoinDiagnostic(SyntaxNode syntax, string reason)
        => DiagnosticInfo.Create(DiagnosticDescriptors.JoinNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);

    private static LambdaExpressionSyntax? LambdaAt(IInvocationOperation invocation, int ordinal)
    {
        var syntax = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal)?.Value.Syntax;
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            syntax = parenthesized.Expression;
        }

        return syntax as LambdaExpressionSyntax;
    }

    /// <summary>
    /// The paths a key selector reads, one per key part: a column, or an anonymous type of columns. Outer keys can read
    /// through reference navigations; inner keys are properties of the inner entity itself, which its join reads.
    /// </summary>
    private static List<string>? KeyPaths(
        LambdaExpressionSyntax lambda, IReadOnlyList<Alias> aliases, bool innerSide, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ParameterOf(lambda, model, cancellationToken) is not { } parameter
            || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return null;
        }

        var parameters = new Dictionary<IParameterSymbol, IReadOnlyList<Alias>>(SymbolEqualityComparer.Default) { [parameter] = aliases };
        var parts = operation is IAnonymousObjectCreationOperation anonymous
            ? anonymous.Initializers.Select(static i => (i as ISimpleAssignmentOperation)?.Value)
            : [operation];

        var paths = new List<string>();
        foreach (var part in parts)
        {
            var value = part;
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            if (value is null || Alias.Resolve(value, parameters) is not { } path || ColumnOf(path) is not var (column, _)
                || innerSide && (path.IsColumn || path.Rest.Count != 1))
            {
                return null;
            }

            paths.Add(column);
        }

        return paths;
    }

    /// <summary>The column path a resolved member chain reads and its type, or null when it reads an entity or past a column.</summary>
    private static (string Path, ITypeSymbol Type)? ColumnOf(AliasedPath path)
    {
        if (path.Column is { } column)
        {
            return (path.Prefix, column.Type);
        }

        if (path.ScalarType is { } scalar)
        {
            return (path.Prefix, scalar);
        }

        var rest = path.Rest;
        if (rest.Count == 0 || !EntityShapeBuilder.IsReadableScalar(rest[rest.Count - 1].Type))
        {
            return null;
        }

        for (var i = 0; i < rest.Count - 1; i++)
        {
            if (EntityShapeBuilder.IsCollectionNavigation(rest[i].Type) || EntityShapeBuilder.NavigationTarget(rest[i].Type) is null)
            {
                return null;
            }
        }

        return (path.Path, rest[rest.Count - 1].Type);
    }

    private static IParameterSymbol? ParameterOf(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
    {
        var syntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        return syntax is null ? null : model.GetDeclaredSymbol(syntax, cancellationToken);
    }

    /// <summary>
    /// Whether the inner sequence of a join is a set of the context, through filters and tracking operators and local
    /// variables, which the runtime reads the inner query from.
    /// </summary>
    private static bool IsFilteredSet(IOperation? source, SemanticModel model, CancellationToken cancellationToken)
    {
        for (var steps = 0; source is not null && steps < 64; steps++)
        {
            switch (source)
            {
                case IConversionOperation conversion:
                    source = conversion.Operand;
                    break;
                case IInvocationOperation invocation when IsDbSet(invocation.Type!):
                    return true;

                // SQL of FromSql, which only a set has, is the inner query's root.
                case IInvocationOperation invocation when GetFamily(invocation.TargetMethod.ContainingType) == Family.Relational
                    && invocation.TargetMethod.Name is "FromSql" or "FromSqlInterpolated" or "FromSqlRaw":
                    return true;
                case IInvocationOperation invocation:
                    var method = invocation.TargetMethod;
                    var family = GetFamily(method.ContainingType);
                    var filters = family == Family.Queryable && method.Name == "Where" && IsPredicate(method.Parameters[1].Type)
                        || family == Family.KetPsi && method.Name == WhereIfMethod
                        || family == Family.EntityFramework
                            && method.Name is "AsNoTracking" or "AsTracking" or "AsNoTrackingWithIdentityResolution" or "IgnoreQueryFilters";
                    if (!filters)
                    {
                        return false;
                    }

                    source = GetSource(invocation);
                    break;
                case ILocalReferenceOperation local:
                    source = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                        {
                            Initializer.Value: { } value,
                        } && value.SyntaxTree == model.SyntaxTree
                        ? model.GetOperation(value, cancellationToken)
                        : null;
                    break;
                default:
                    return source.Type is { } type && IsDbSet(type);
            }
        }

        return false;
    }

    private sealed partial class SiteBuilder
    {
        /// <summary>A <c>Join</c> of the entity query, whose own parameter is the queried entity.</summary>
        public CallSite JoinOnEntity() => JoinOn(Alias.At("", elementSymbol), index: 0);

        /// <summary>A <c>Join</c> whose outer parameter stands for <paramref name="outer"/>, as the chain's join number <paramref name="index"/>.</summary>
        public CallSite JoinOn(IReadOnlyList<Alias> outer, int index)
        {
            foreach (var ordinal in new[] { 2, 3, 4 })
            {
                if (RequireLambda(ordinal) is (null, { } lambdaDiagnostic))
                {
                    return Failed(lambdaDiagnostic);
                }
            }

            var (translation, diagnostic) = TranslateJoin(operation, outer, index, semanticModel, location, cancellationToken);
            if (translation is not { } join)
            {
                return diagnostic is null ? Unsupported() : Failed(diagnostic);
            }

            return new CallSite(
                _elementType,
                location,
                OperatorKind.Join,
                IsAsync: false,
                HasPredicate: false,
                Diagnostic: null,
                Entity: projectedFrom is null && elementSymbol.TypeKind == TypeKind.Class
                    ? EntityShapeBuilder.Build(elementSymbol, semanticModel.Compilation, location)
                    : null,
                Interception: semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Signature: GetGenericSignature(operation.TargetMethod),
                Projection: join.Projection,
                ProjectedFrom: projectedFrom,
                Join: join.Join);
        }

        /// <summary>
        /// <c>Where</c>, an ordering or <c>Select</c> on a projection, reading its members through what they stand for, so
        /// the predicate, the key and the selected paths are the queried entity's and the joined ones'.
        /// </summary>
        public CallSite OnAliases(OperatorKind kind, IReadOnlyList<Alias> aliases)
        {
            var (lambda, lambdaDiagnostic) = RequireLambda(1);
            if (lambdaDiagnostic is not null)
            {
                return Failed(lambdaDiagnostic);
            }

            if (lambda is null || ParameterOf(lambda, semanticModel, cancellationToken) is not { } parameter)
            {
                return Unsupported();
            }

            var parameters = new Dictionary<IParameterSymbol, IReadOnlyList<Alias>>(SymbolEqualityComparer.Default) { [parameter] = aliases };
            PredicateModel? predicate = null;
            string? orderProperty = null;
            ProjectionModel? projection = null;
            switch (kind)
            {
                case OperatorKind.Where:
                    PredicateOperand? Operand(ExpressionSyntax node)
                        => semanticModel.GetOperation(node, cancellationToken) is { } read
                            && Alias.Resolve(read, parameters) is { } path
                            && ColumnOf(path) is var (column, type)
                                ? new PredicateOperand(column, type)
                                : null;

                    var (translated, predicateDiagnostic) = PredicateTranslator.Translate(lambda, semanticModel, cancellationToken, operands: Operand);
                    if (translated is null)
                    {
                        return Failed(predicateDiagnostic!);
                    }

                    predicate = translated;
                    break;

                case OperatorKind.Select:
                    var (selected, _, projectionDiagnostic) = ProjectionTranslator.Translate(lambda, [aliases], semanticModel, location, cancellationToken);
                    if (selected is null)
                    {
                        return Failed(projectionDiagnostic!);
                    }

                    projection = selected;
                    break;

                default:
                    var key = lambda.Body is ExpressionSyntax body ? semanticModel.GetOperation(body, cancellationToken) : null;
                    while (key is IConversionOperation conversion)
                    {
                        key = conversion.Operand;
                    }

                    if (key is null || Alias.Resolve(key, parameters) is not { } keyPath || ColumnOf(keyPath) is not var (keyColumn, _))
                    {
                        var node = lambda.Body as ExpressionSyntax ?? (SyntaxNode)lambda;
                        return Failed(DiagnosticInfo.Create(
                            DiagnosticDescriptors.OrderKeyNotSupported, LocationInfo.From(node.GetLocation()), node.ToString()));
                    }

                    orderProperty = keyColumn;
                    break;
            }

            return new CallSite(
                _elementType,
                location,
                kind,
                IsAsync: false,
                HasPredicate: false,
                Diagnostic: null,
                Interception: semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Predicate: predicate,
                Signature: GetGenericSignature(operation.TargetMethod),
                OrderProperty: orderProperty,
                UsesDecimalOperators: kind == OperatorKind.Where && DecimalOperators.AreUsedIn(lambda, semanticModel, cancellationToken),
                Projection: projection,
                ProjectedFrom: projectedFrom);
        }
    }
}

/// <summary>A translated <c>Join</c>: its keys, its result selector, and what the result's members stand for.</summary>
internal readonly record struct JoinTranslation(JoinModel Join, ProjectionModel Projection, IReadOnlyList<Alias> Aliases);
