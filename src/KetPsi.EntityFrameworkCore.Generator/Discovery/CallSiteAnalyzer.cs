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
    private const string ReasonComparer = "custom comparers are not supported";
    private const string ReasonRangeCount = "only an int count is supported";
    private const string ReasonDefaultValue = "overloads with a default value are not supported";

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
        var site = new SiteBuilder(
            elementType, LocationInfo.From(nameSyntax.GetLocation()), method.Name, operation, invocation, semanticModel, cancellationToken);

        return family switch
        {
            Family.Queryable => ClassifyQueryable(site, method),
            Family.EntityFramework => ClassifyEntityFramework(site, method),
            Family.Relational => method.Name.StartsWith("FromSql", StringComparison.Ordinal)
                ? site.Reject(DiagnosticDescriptors.RawSqlNotSupported, method.Name)
                : site.Unsupported(),
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
            case "IgnoreQueryFilters":
                return site.Supported(OperatorKind.IgnoreQueryFilters);
            case "AsTracking":
                return site.Reject(DiagnosticDescriptors.TrackingNotSupported, $"'{method.Name}' is not supported");
            case "Include" or "ThenInclude":
                return site.Reject(DiagnosticDescriptors.IncludeNotSupported, method.Name);
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
    private static ITypeSymbol? GetSourceType(IInvocationOperation operation)
    {
        var source = operation.TargetMethod.IsStatic
            ? operation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value
            : operation.Instance;

        while (source is IConversionOperation { IsImplicit: true } conversion)
        {
            source = conversion.Operand;
        }

        return source?.Type;
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
        CancellationToken cancellationToken)
    {
        private readonly string _elementType = EntityDiscovery.TypeName(elementSymbol);

        /// <param name="kind">The operator.</param>
        /// <param name="lambdaOrdinal">The parameter that must be a lambda written at the call site.</param>
        /// <param name="hasPredicate">Whether that lambda is a predicate, which is translated here.</param>
        /// <param name="isAsync">Whether the operator is an async terminal.</param>
        public CallSite Supported(OperatorKind kind, int? lambdaOrdinal = null, bool hasPredicate = false, bool isAsync = false)
        {
            PredicateModel? predicate = null;
            string? orderProperty = null;
            if (lambdaOrdinal is { } ordinal)
            {
                var (lambda, lambdaDiagnostic) = RequireLambda(ordinal);
                if (lambdaDiagnostic is not null)
                {
                    return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, lambdaDiagnostic);
                }

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
            }

            // Only classes can be entities; the shape is kept only if the element type turns out to be one.
            var entity = elementSymbol.TypeKind == TypeKind.Class
                ? EntityShapeBuilder.Build(elementSymbol, semanticModel.Compilation, location)
                : null;
            var interception = semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax();
            var runsTrackedQuery = TrackingAnalysis.ReturnsEntities(kind)
                && TrackingAnalysis.RunsTrackedQuery(invocation, semanticModel, cancellationToken);

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
                GetSignature(operation.TargetMethod),
                orderProperty,
                runsTrackedQuery);
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

        public CallSite Unsupported() => Reject(DiagnosticDescriptors.UnsupportedOperator, methodName);

        public CallSite Overload(string reason) => Reject(DiagnosticDescriptors.UnsupportedOverload, methodName, reason);

        public CallSite Reject(DiagnosticDescriptor descriptor, params string[] messageArgs)
            => new(_elementType, location, Operator: null, IsAsync: false, HasPredicate: false, DiagnosticInfo.Create(descriptor, location, messageArgs));

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
