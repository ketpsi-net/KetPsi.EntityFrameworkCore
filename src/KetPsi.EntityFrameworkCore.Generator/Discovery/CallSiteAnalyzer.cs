using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Classifies calls to query operators: each call becomes a supported operator or a diagnostic. Whether the
/// query is an entity query is decided later, once the entity types are known.
/// </summary>
internal static class CallSiteAnalyzer
{
    /// <summary>The element type used for internal errors, which are reported whatever the query is.</summary>
    public const string UnknownElementType = "?";

    private const string WhereIfMethod = "WhereIf";
    private const string ReasonIndexedPredicate = "predicates that take the element index are not supported";
    private const string ReasonIndexedSelector = "selectors that take the element index are not supported";
    private const string ReasonComparer = "custom comparers are not supported";
    private const string ReasonRangeCount = "only an int count is supported";
    private const string ReasonDefaultValue = "overloads with a default value are not supported";
    private const string ReasonAfterSelect =
        "only Skip, Take and terminals without a predicate can follow Select; filter and order the entities before it";

    // Names of every method the classification below handles; anything else is not looked at.
    private static readonly HashSet<string> OperatorNames = new(StringComparer.Ordinal)
    {
        // System.Linq.Queryable
        "Aggregate", "AggregateBy", "All", "Any", "Append", "AsQueryable", "Average", "Cast", "Chunk", "Concat", "Contains",
        "Count", "CountBy", "DefaultIfEmpty", "Distinct", "DistinctBy", "ElementAt", "ElementAtOrDefault", "Except", "ExceptBy",
        "First", "FirstOrDefault", "GroupBy", "GroupJoin", "Index", "Intersect", "IntersectBy", "Join", "Last", "LastOrDefault",
        "LeftJoin", "LongCount", "Max", "MaxBy", "Min", "MinBy", "OfType", "Order", "OrderBy", "OrderByDescending",
        "OrderDescending", "Prepend", "Reverse", "RightJoin", "Select", "SelectMany", "SequenceEqual", "Single",
        "SingleOrDefault", "Skip", "SkipLast", "SkipWhile", "Sum", "Take", "TakeLast", "TakeWhile", "ThenBy",
        "ThenByDescending", "Union", "UnionBy", "Where", "Zip",

        // EF Core and providers
        "AllAsync", "AnyAsync", "AsAsyncEnumerable", "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsSingleQuery",
        "AsSplitQuery", "AsTracking", "AverageAsync", "ContainsAsync", "CountAsync", "ElementAtAsync", "ElementAtOrDefaultAsync",
        "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync", "Find", "FindAsync", "FirstAsync",
        "FirstOrDefaultAsync", "ForEachAsync", "FromSql", "FromSqlInterpolated", "FromSqlRaw", "IgnoreAutoIncludes",
        "IgnoreQueryFilters", "Include", "LastAsync", "LastOrDefaultAsync", "Load", "LoadAsync", "LongCountAsync", "MaxAsync",
        "MinAsync", "SingleAsync", "SingleOrDefaultAsync", "SumAsync", "TagWith", "TagWithCallSite", "TemporalAll",
        "TemporalAsOf", "TemporalBetween", "TemporalContainedIn", "TemporalFromTo", "ThenInclude", "ToArrayAsync",
        "ToDictionaryAsync", "ToHashSetAsync", "ToListAsync", "ToQueryString",

        // System.Linq.Enumerable on a query
        "AsEnumerable", "ToArray", "ToDictionary", "ToHashSet", "ToList", "ToLookup",

        // KetPsi
        WhereIfMethod,
    };

    private static readonly SymbolDisplayFormat SignatureFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private enum Family
    {
        None,
        Queryable,
        EntityFramework,
        Relational,
        SqlServer,
        KetPsi,
        Enumerable,
        DbSet,
    }

    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
        => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            && OperatorNames.Contains(memberAccess.Name.Identifier.ValueText);

    public static CallSite? Analyze(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        => Analyze((InvocationExpressionSyntax)context.Node, context.SemanticModel, cancellationToken);

    /// <summary>Classifies one call; <see langword="null"/> when it is not an operator on a query.</summary>
    public static CallSite? Analyze(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        try
        {
            return Classify(invocation, semanticModel, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return InternalError(invocation, exception);
        }
    }

    internal static CallSite InternalError(InvocationExpressionSyntax invocation, Exception exception)
        => new(
            UnknownElementType,
            LocationInfo.From(invocation.GetLocation()),
            Operator: null,
            IsAsync: false,
            HasPredicate: false,
            DiagnosticInfo.Create(DiagnosticDescriptors.InternalError, LocationInfo.From(invocation.GetLocation()), exception.Message));

    private static CallSite? Classify(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return null;
        }

        var method = operation.TargetMethod;
        var family = GetFamily(method.ContainingType);
        if (family == Family.None)
        {
            return null;
        }

        var sourceType = GetSourceType(operation);
        var queryableInterface = semanticModel.Compilation.GetTypeByMetadataName(KnownTypes.QueryableInterface);
        if (sourceType is null || queryableInterface is null || GetElementType(sourceType, queryableInterface) is not { } elementType)
        {
            return null;
        }

        var nameSyntax = ((MemberAccessExpressionSyntax)invocation.Expression).Name;
        var projectedFrom = ProjectedFrom(GetSource(operation), semanticModel, queryableInterface, cancellationToken);
        var site = new SiteBuilder(
            elementType, LocationInfo.From(nameSyntax.Identifier.GetLocation()), method.Name, operation, invocation, semanticModel, projectedFrom, cancellationToken);

        if (projectedFrom is not null)
        {
            return ClassifyProjected(site, method, family);
        }

        return family switch
        {
            Family.Queryable => ClassifyQueryable(site, method),
            Family.EntityFramework => ClassifyEntityFramework(site, method),
            Family.Relational => method.Name switch
            {
                "AsSplitQuery" => site.Supported(OperatorKind.AsSplitQuery),
                "AsSingleQuery" => site.Supported(OperatorKind.AsSingleQuery),
                _ when method.Name.StartsWith("FromSql", StringComparison.Ordinal) => site.Reject(DiagnosticDescriptors.RawSqlNotSupported, method.Name),
                _ => site.Unsupported(),
            },
            Family.KetPsi => method.Name == WhereIfMethod ? site.Supported(OperatorKind.WhereIf, lambdaOrdinal: 2) : null,
            Family.Enumerable => ClassifyEnumerable(site, method, sourceType),
            Family.SqlServer => site.Unsupported(),

            // Of DbSet's own methods, only these query the database through EF Core's pipeline.
            Family.DbSet => method.Name is "Find" or "FindAsync" or "AsAsyncEnumerable" ? site.Unsupported() : null,
            _ => null,
        };
    }

    private static CallSite ClassifyQueryable(SiteBuilder site, IMethodSymbol method)
    {
        var parameters = method.Parameters;
        switch (method.Name)
        {
            case "Where":
                return IsPredicate(parameters[1].Type)
                    ? site.Supported(OperatorKind.Where, lambdaOrdinal: 1)
                    : site.Overload(ReasonIndexedPredicate);
            case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
                return parameters.Length == 2
                    ? site.Supported(OrderKind(method.Name), lambdaOrdinal: 1)
                    : site.Overload(ReasonComparer);
            case "Skip" or "Take":
                return parameters[1].Type.SpecialType == SpecialType.System_Int32
                    ? site.Supported(method.Name == "Skip" ? OperatorKind.Skip : OperatorKind.Take)
                    : site.Overload(ReasonRangeCount);
            case "Select":
                return IsSelector(parameters[1].Type)
                    ? site.Supported(OperatorKind.Select, lambdaOrdinal: 1)
                    : site.Overload(ReasonIndexedSelector);
            case "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Any" or "Count" or "LongCount":
                if (parameters.Length == 1)
                {
                    return site.Supported(TerminalKind(method.Name));
                }

                return parameters.Length == 2 && IsPredicate(parameters[1].Type)
                    ? site.Supported(TerminalKind(method.Name), lambdaOrdinal: 1, hasPredicate: true)
                    : site.Overload(ReasonDefaultValue);
            default:
                return site.Unsupported();
        }
    }

    private static CallSite ClassifyEntityFramework(SiteBuilder site, IMethodSymbol method)
    {
        var parameters = method.Parameters;
        switch (method.Name)
        {
            case "AsNoTracking":
                return site.Supported(OperatorKind.AsNoTracking);
            case "AsNoTrackingWithIdentityResolution":
                return site.Supported(OperatorKind.AsNoTrackingWithIdentityResolution);
            case "AsTracking":
                return site.Supported(OperatorKind.AsTracking);
            case "IgnoreQueryFilters":
                return site.Supported(OperatorKind.IgnoreQueryFilters);
            case "Include" or "ThenInclude":
                var include = method.Name == "Include" ? OperatorKind.Include : OperatorKind.ThenInclude;
                return parameters[1].Type.SpecialType == SpecialType.System_String
                    ? site.RejectArgument(DiagnosticDescriptors.IncludePathNotSupported, ordinal: 1, IncludePathTranslator.ReasonStringPath)
                    : site.Supported(include, lambdaOrdinal: 1);
            case "ToListAsync" or "ToArrayAsync":
                return site.Supported(method.Name == "ToListAsync" ? OperatorKind.ToList : OperatorKind.ToArray, isAsync: true);
            case "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync" or "AnyAsync" or "CountAsync"
                or "LongCountAsync":
                var kind = TerminalKind(method.Name.Substring(0, method.Name.Length - "Async".Length));
                return parameters.Length == 3 && IsPredicate(parameters[1].Type)
                    ? site.Supported(kind, lambdaOrdinal: 1, hasPredicate: true, isAsync: true)
                    : site.Supported(kind, isAsync: true);
            default:
                return site.Unsupported();
        }
    }

    // After Select the query is a projection: paging and terminals run it, anything else is reported.
    private static CallSite? ClassifyProjected(SiteBuilder site, IMethodSymbol method, Family family)
    {
        var parameters = method.Parameters;
        switch (family)
        {
            case Family.Queryable when method.Name is "Skip" or "Take" && parameters[1].Type.SpecialType == SpecialType.System_Int32:
                return site.SupportedAfterSelect(method.Name == "Skip" ? OperatorKind.Skip : OperatorKind.Take);
            case Family.Queryable when parameters.Length == 1
                && method.Name is "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Any" or "Count" or "LongCount":
                return site.SupportedAfterSelect(TerminalKind(method.Name));
            case Family.EntityFramework when method.Name is "ToListAsync" or "ToArrayAsync":
                return site.SupportedAfterSelect(method.Name == "ToListAsync" ? OperatorKind.ToList : OperatorKind.ToArray, isAsync: true);
            case Family.EntityFramework when parameters.Length == 2
                && method.Name is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync" or "AnyAsync"
                    or "CountAsync" or "LongCountAsync":
                return site.SupportedAfterSelect(TerminalKind(method.Name.Substring(0, method.Name.Length - "Async".Length)), isAsync: true);
            case Family.Enumerable:
                // Enumerating a projected query runs it, so only the terminals that would otherwise copy it are intercepted.
                return parameters.Length == 1 && method.Name is "ToList" or "ToArray"
                    ? site.SupportedAfterSelect(method.Name == "ToList" ? OperatorKind.ToList : OperatorKind.ToArray)
                    : null;
            case Family.DbSet:
                return null;
            default:
                return site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonAfterSelect);
        }
    }

    /// <summary>
    /// The entity query a projection was made from: the source of the <c>Select</c> that <paramref name="source"/>
    /// comes from, through other operators and local variables. <see langword="null"/> when it isn't a projection.
    /// </summary>
    private static string? ProjectedFrom(IOperation? source, SemanticModel semanticModel, INamedTypeSymbol queryableInterface, CancellationToken cancellationToken)
    {
        // A bound guards against a local initialized from itself through other locals.
        for (var steps = 0; source is not null && steps < 64; steps++)
        {
            switch (source)
            {
                case IConversionOperation conversion:
                    source = conversion.Operand;
                    break;
                case IParenthesizedOperation parenthesized:
                    source = parenthesized.Operand;
                    break;
                case IInvocationOperation invocation when GetFamily(invocation.TargetMethod.ContainingType) is var family
                    && family is Family.Queryable or Family.EntityFramework or Family.Relational or Family.KetPsi:
                    if (family == Family.Queryable && invocation.TargetMethod.Name == "Select")
                    {
                        return GetSource(invocation)?.Type is { } selected && GetElementType(selected, queryableInterface) is { } element
                            ? EntityDiscovery.TypeName(element)
                            : null;
                    }

                    source = GetSource(invocation);
                    break;
                case ILocalReferenceOperation local:
                    source = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                        {
                            Initializer.Value: { } value,
                        } && value.SyntaxTree == semanticModel.SyntaxTree
                        ? semanticModel.GetOperation(value, cancellationToken)
                        : null;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    // Enumerable.ToList and ToArray execute the query, so they are intercepted like terminals. Other Enumerable
    // methods enumerate a KetPsi query, which executes it; called on a DbSet itself they would run EF Core's pipeline.
    private static CallSite? ClassifyEnumerable(SiteBuilder site, IMethodSymbol method, ITypeSymbol sourceType)
    {
        if (method.Parameters.Length == 1 && method.Name is "ToList" or "ToArray")
        {
            return site.Supported(method.Name == "ToList" ? OperatorKind.ToList : OperatorKind.ToArray);
        }

        return IsDbSet(sourceType) ? site.Unsupported() : null;
    }

    private static Family GetFamily(INamedTypeSymbol type) => type.OriginalDefinition.ToDisplayString() switch
    {
        KnownTypes.Queryable => Family.Queryable,
        KnownTypes.EntityFrameworkQueryableExtensions => Family.EntityFramework,
        KnownTypes.RelationalQueryableExtensions => Family.Relational,
        KnownTypes.SqlServerQueryableExtensions or KnownTypes.SqlServerDbSetExtensions => Family.SqlServer,
        KnownTypes.KetPsiQueryableExtensions => Family.KetPsi,
        KnownTypes.Enumerable => Family.Enumerable,
        KnownTypes.DbSetDefinition => Family.DbSet,
        _ => Family.None,
    };

    // The query the operator is applied to: the receiver of an instance method, otherwise the first argument.
    private static ITypeSymbol? GetSourceType(IInvocationOperation operation) => GetSource(operation)?.Type;

    private static IOperation? GetSource(IInvocationOperation operation)
    {
        var source = operation.TargetMethod.IsStatic
            ? operation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value
            : operation.Instance;

        while (source is IConversionOperation { IsImplicit: true } conversion)
        {
            source = conversion.Operand;
        }

        return source;
    }

    // The T of IQueryable<T>; null for other types and for type parameters, which generic helpers use.
    private static INamedTypeSymbol? GetElementType(ITypeSymbol type, INamedTypeSymbol queryableInterface)
    {
        if (type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, queryableInterface))
        {
            return named.TypeArguments[0] as INamedTypeSymbol;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, queryableInterface))
            {
                return implemented.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static bool IsDbSet(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == KnownTypes.DbSetDefinition)
            {
                return true;
            }
        }

        return false;
    }

    // Expression<Func<T, bool>>, as opposed to the index-taking Expression<Func<T, int, bool>>.
    private static bool IsPredicate(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeArguments.Length: 1 } expression
            && expression.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 2 } func
            && func.TypeArguments[1].SpecialType == SpecialType.System_Boolean;

    // Expression<Func<T, TResult>>, as opposed to the index-taking Expression<Func<T, int, TResult>>.
    private static bool IsSelector(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeArguments.Length: 1 } expression
            && expression.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 2 };

    private static OperatorKind OrderKind(string name) => name switch
    {
        "OrderBy" => OperatorKind.OrderBy,
        "OrderByDescending" => OperatorKind.OrderByDescending,
        "ThenBy" => OperatorKind.ThenBy,
        _ => OperatorKind.ThenByDescending,
    };

    private static OperatorKind TerminalKind(string name) => name switch
    {
        "First" => OperatorKind.First,
        "FirstOrDefault" => OperatorKind.FirstOrDefault,
        "Single" => OperatorKind.Single,
        "SingleOrDefault" => OperatorKind.SingleOrDefault,
        "Any" => OperatorKind.Any,
        "Count" => OperatorKind.Count,
        _ => OperatorKind.LongCount,
    };

    private sealed class SiteBuilder(
        INamedTypeSymbol elementSymbol,
        LocationInfo location,
        string methodName,
        IInvocationOperation operation,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string? projectedFrom,
        CancellationToken cancellationToken)
    {
        private readonly string _elementType = EntityDiscovery.TypeName(elementSymbol);

        /// <summary>
        /// An operator on a projected query. Its interceptor is generic, since the element type can be anonymous, and
        /// reads no entity.
        /// </summary>
        public CallSite SupportedAfterSelect(OperatorKind kind, bool isAsync = false)
            => new(
                _elementType,
                location,
                kind,
                isAsync,
                HasPredicate: false,
                Diagnostic: null,
                Interception: semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Signature: GetGenericSignature(operation.TargetMethod),
                ProjectedFrom: projectedFrom);

        /// <param name="kind">The operator.</param>
        /// <param name="lambdaOrdinal">The parameter that must be a lambda written at the call site.</param>
        /// <param name="hasPredicate">Whether that lambda is a predicate, which is translated here.</param>
        /// <param name="isAsync">Whether the operator is an async terminal.</param>
        public CallSite Supported(OperatorKind kind, int? lambdaOrdinal = null, bool hasPredicate = false, bool isAsync = false)
        {
            PredicateModel? predicate = null;
            string? orderProperty = null;
            IncludeModel? include = null;
            ProjectionModel? projection = null;
            var usesDecimalOperators = false;
            if (lambdaOrdinal is { } ordinal)
            {
                var (lambda, lambdaDiagnostic) = RequireLambda(ordinal);
                if (lambdaDiagnostic is not null)
                {
                    return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, lambdaDiagnostic);
                }

                usesDecimalOperators = lambda is not null && DecimalOperators.AreUsedIn(lambda, semanticModel, cancellationToken);

                if (lambda is not null && IsPredicate(kind, hasPredicate))
                {
                    var (translated, translationDiagnostic) = PredicateTranslator.Translate(lambda, semanticModel, cancellationToken);
                    if (translationDiagnostic is not null)
                    {
                        return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, translationDiagnostic);
                    }

                    predicate = translated;
                }
                else if (lambda is not null && IsOrdering(kind))
                {
                    var (property, keyDiagnostic) = KeySelectorTranslator.Translate(lambda, semanticModel, cancellationToken);
                    if (keyDiagnostic is not null)
                    {
                        return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, keyDiagnostic);
                    }

                    orderProperty = property;
                }
                else if (lambda is not null && kind == OperatorKind.Select)
                {
                    var (translated, projectionDiagnostic) = ProjectionTranslator.Translate(
                        lambda, elementSymbol, semanticModel, location, cancellationToken);
                    if (projectionDiagnostic is not null)
                    {
                        return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, projectionDiagnostic);
                    }

                    projection = translated;
                }
                else if (lambda is not null && kind is OperatorKind.Include or OperatorKind.ThenInclude)
                {
                    var (path, pathDiagnostic) = IncludePathTranslator.Translate(lambda, semanticModel, cancellationToken);
                    if (pathDiagnostic is not null)
                    {
                        return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, pathDiagnostic);
                    }

                    // The operator's last type argument is TProperty, the navigation property's type.
                    var compilation = semanticModel.Compilation;
                    include = new IncludeModel(
                        path!.Navigation,
                        operation.TargetMethod.TypeArguments[operation.TargetMethod.TypeArguments.Length - 1].ToDisplayString(SignatureFormat),
                        EntityShapeBuilder.Build(path.Source, compilation, location),
                        EntityShapeBuilder.Build(path.Target, compilation, location));
                }
            }

            // Only classes can be entities; the shape is kept only if the element type turns out to be one.
            var entity = elementSymbol.TypeKind == TypeKind.Class
                ? EntityShapeBuilder.Build(elementSymbol, semanticModel.Compilation, location)
                : null;
            var interception = semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax();

            return new CallSite(
                _elementType,
                location,
                kind,
                isAsync,
                hasPredicate,
                Diagnostic: null,
                entity,
                interception,
                predicate,
                kind == OperatorKind.Select ? GetGenericSignature(operation.TargetMethod) : GetSignature(operation.TargetMethod),
                orderProperty,
                include,
                usesDecimalOperators,
                projection);
        }

        private static bool IsPredicate(OperatorKind kind, bool hasPredicate)
            => hasPredicate || kind is OperatorKind.Where or OperatorKind.WhereIf;

        private static bool IsOrdering(OperatorKind kind)
            => kind is OperatorKind.OrderBy or OperatorKind.OrderByDescending or OperatorKind.ThenBy or OperatorKind.ThenByDescending;

        // The interceptor matches the method as declared, with the call's type arguments substituted.
        private static InterceptorSignature GetSignature(IMethodSymbol method)
        {
            var declared = method.ReducedFrom is null ? method : method.GetConstructedReducedFrom() ?? method;
            return new InterceptorSignature(
                declared.Name,
                EntityDiscovery.TypeName(declared.ContainingType),
                declared.ReturnType.ToDisplayString(SignatureFormat),
                declared.Parameters.Select(static p => new ParameterSignature(p.Type.ToDisplayString(SignatureFormat), p.Name)).ToEquatableArray());
        }

        // The method as declared, before its type arguments are substituted, for a generic interceptor.
        private static InterceptorSignature GetGenericSignature(IMethodSymbol method)
        {
            var declared = (method.ReducedFrom ?? method).OriginalDefinition;
            return new InterceptorSignature(
                declared.Name,
                EntityDiscovery.TypeName(declared.ContainingType),
                declared.ReturnType.ToDisplayString(SignatureFormat),
                declared.Parameters.Select(static p => new ParameterSignature(p.Type.ToDisplayString(SignatureFormat), p.Name)).ToEquatableArray(),
                declared.TypeParameters.Select(static t => t.Name).ToEquatableArray());
        }

        public CallSite Unsupported() => Reject(DiagnosticDescriptors.UnsupportedOperator, methodName);

        /// <summary>Rejects the call at the argument with <paramref name="ordinal"/>, quoting it in the message.</summary>
        public CallSite RejectArgument(DiagnosticDescriptor descriptor, int ordinal, string reason)
        {
            var argument = operation.Arguments.First(a => a.Parameter?.Ordinal == ordinal).Value.Syntax;
            var argumentLocation = LocationInfo.From(argument.GetLocation());
            return new(_elementType, argumentLocation, Operator: null, IsAsync: false, HasPredicate: false,
                DiagnosticInfo.Create(descriptor, argumentLocation, argument.ToString(), reason));
        }

        public CallSite Overload(string reason) => Reject(DiagnosticDescriptors.UnsupportedOverload, methodName, reason);

        // A rejected operator on a projection is reported when the projection is of an entity query.
        public CallSite Reject(DiagnosticDescriptor descriptor, params string[] messageArgs)
            => new(
                _elementType,
                location,
                Operator: null,
                IsAsync: false,
                HasPredicate: false,
                DiagnosticInfo.Create(descriptor, location, messageArgs),
                ProjectedFrom: projectedFrom);

        // Lambdas are translated at build time, so the argument must be the lambda itself.
        private (LambdaExpressionSyntax? Lambda, DiagnosticInfo? Diagnostic) RequireLambda(int ordinal)
        {
            var argument = operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal);
            if (argument?.Value.Syntax is not { } syntax)
            {
                return (null, null);
            }

            while (syntax is ParenthesizedExpressionSyntax parenthesized)
            {
                syntax = parenthesized.Expression;
            }

            return syntax is LambdaExpressionSyntax lambda
                ? (lambda, null)
                : (null, DiagnosticInfo.Create(
                    DiagnosticDescriptors.LambdaRequired,
                    LocationInfo.From(argument.Value.Syntax.GetLocation()),
                    argument.Parameter!.Name,
                    methodName));
        }
    }
}
