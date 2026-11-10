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
internal static partial class CallSiteAnalyzer
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
        "only Skip, Take, Distinct, aggregates and terminals without a predicate can follow Select; filter and order the entities before it";
    private const string ReasonDistinctComputed =
        "Distinct compares the projected values in SQL, so the projection cannot compute values in C#";
    private const string ReasonAggregateProjection =
        "an aggregate works on a projection of one property, such as Select(x => x.Price).Sum()";
    private const string ReasonAggregateSelector =
        "an aggregate's selector must read one property, such as x => x.Price, or one through reference navigations";
    private const string ReasonSelectorAfterSelect = "select the aggregated property instead, such as Select(x => x.Price).Sum()";
    private const string ReasonAfterGroupPaging = "filter and order the groups before Skip, Take and Distinct";
    private const string ReasonOnGroups =
        "only Where, OrderBy, ThenBy, Select, Count, LongCount, Any, ToList and ToArray can follow GroupBy; select the key " +
        "and aggregates with Select, then page them or take the first";
    private const string ReasonGroupsAfterFilter =
        "groups filtered or ordered by the database are only read through a Select of their key and aggregates";
    private const string GroupingType = "System.Linq.IGrouping<TKey, TElement>";

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
        var location = LocationInfo.From(nameSyntax.Identifier.GetLocation());
        if (IsGrouping(elementType))
        {
            if (GroupedFrom(GetSource(operation), semanticModel, queryableInterface, location, cancellationToken) is not { } grouped)
            {
                return null;
            }

            var groupSite = new SiteBuilder(elementType, location, method.Name, operation, invocation, semanticModel, grouped.EntityType, cancellationToken);
            return ClassifyGrouped(groupSite, method, family, grouped);
        }

        var projectedFrom = ProjectedFrom(GetSource(operation), semanticModel, queryableInterface, location, cancellationToken);
        var site = new SiteBuilder(
            elementType, location, method.Name, operation, invocation, semanticModel, projectedFrom?.EntityType, cancellationToken);

        if (projectedFrom is { } projection)
        {
            return ClassifyProjected(site, method, family, projection);
        }

        return family switch
        {
            Family.Queryable => ClassifyQueryable(site, method),
            Family.EntityFramework => ClassifyEntityFramework(site, method),
            Family.Relational => method.Name switch
            {
                "AsSplitQuery" => site.Supported(OperatorKind.AsSplitQuery),
                "AsSingleQuery" => site.Supported(OperatorKind.AsSingleQuery),

                // EF Core 8 declares ExecuteDelete with the relational extensions, EF Core 9 and later with the others.
                "ExecuteDelete" => site.Supported(OperatorKind.ExecuteDelete),
                "ExecuteDeleteAsync" => site.Supported(OperatorKind.ExecuteDelete, isAsync: true),
                // The SQL is a value of the call, read when the query runs; the operators after it compose over it.
                "FromSql" or "FromSqlInterpolated" => site.Supported(OperatorKind.FromSql),
                "FromSqlRaw" => site.Supported(OperatorKind.FromSqlRaw),
                _ => site.Unsupported(),
            },
            Family.KetPsi => method.Name == WhereIfMethod ? site.Supported(OperatorKind.WhereIf, lambdaOrdinal: 2) : null,
            Family.Enumerable => ClassifyEnumerable(site, method, sourceType),
            Family.SqlServer => site.Unsupported(),

            // Of DbSet's own methods, only these query the database through EF Core's pipeline.
            Family.DbSet => method.Name switch
            {
                "Find" => site.Find(isAsync: false),
                "FindAsync" => site.Find(isAsync: true),
                "AsAsyncEnumerable" => site.Unsupported(),
                _ => null,
            },
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
            case "Distinct":
                return parameters.Length == 1 ? site.Supported(OperatorKind.Distinct) : site.Overload(ReasonComparer);
            case "GroupBy":
                return parameters.Skip(1).All(static p => IsLambdaParameter(p.Type)) ? site.GroupBy() : site.Overload(ReasonComparer);
            case "Join":
                return parameters.Length == 5 ? site.JoinOnEntity() : site.Overload(ReasonComparer);
            case "Sum" or "Average" or "Min" or "Max" when parameters.Length == 2 && IsSelector(parameters[1].Type):
                return site.Supported(AggregateKind(method.Name), lambdaOrdinal: 1);
            case "First" or "FirstOrDefault" or "Last" or "LastOrDefault" or "Single" or "SingleOrDefault" or "Any" or "Count"
                or "LongCount":
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
            case "ExecuteDelete":
                return site.Supported(OperatorKind.ExecuteDelete);
            case "ExecuteDeleteAsync":
                return site.Supported(OperatorKind.ExecuteDelete, isAsync: true);
            case "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync" when parameters.Length == 3 && IsSelector(parameters[1].Type):
                return site.Supported(AggregateKind(method.Name.Substring(0, method.Name.Length - "Async".Length)), lambdaOrdinal: 1, isAsync: true);
            case "FirstAsync" or "FirstOrDefaultAsync" or "LastAsync" or "LastOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync"
                or "AnyAsync" or "CountAsync" or "LongCountAsync":
                var kind = TerminalKind(method.Name.Substring(0, method.Name.Length - "Async".Length));
                return parameters.Length == 3 && IsPredicate(parameters[1].Type)
                    ? site.Supported(kind, lambdaOrdinal: 1, hasPredicate: true, isAsync: true)
                    : site.Supported(kind, isAsync: true);
            default:
                return site.Unsupported();
        }
    }

    // After Select the query is a projection: paging, Distinct, aggregates and terminals run it, anything else is reported.
    // The projection is null when the Select itself is reported.
    private static CallSite? ClassifyProjected(SiteBuilder site, IMethodSymbol method, Family family, ProjectionSource source)
    {
        var (projection, isPaged) = (source.Projection, source.IsPaged);
        var parameters = method.Parameters;
        if (projection is { IsGrouped: true } grouped && family == Family.Queryable
            && (method.Name == "Where" && IsPredicate(parameters[1].Type)
                || method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" && parameters.Length == 2))
        {
            return isPaged
                ? site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonAfterGroupPaging)
                : site.AfterGroupSelect(method.Name == "Where" ? OperatorKind.Where : OrderKind(method.Name), grouped);
        }

        // Operators that read through the projection's members, such as the pair of a query-syntax join.
        if (family == Family.Queryable && source.Aliases is { Count: > 0 } aliases && AliasOperator(method) is { } aliasKind)
        {
            if (isPaged)
            {
                return site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonAliasAfterPaging);
            }

            return aliasKind == OperatorKind.Join ? site.JoinOn(aliases, source.JoinCount) : site.OnAliases(aliasKind, aliases);
        }

        switch (family)
        {
            case Family.Queryable when method.Name == "Distinct" && parameters.Length == 1:
                return projection is { HasComputedValues: true }
                    ? site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonDistinctComputed)
                    : site.SupportedAfterSelect(OperatorKind.Distinct);
            case Family.Queryable or Family.EntityFramework when AggregateName(method.Name) is { } aggregate:
                var isAsync = family == Family.EntityFramework;
                if (parameters.Length != (isAsync ? 2 : 1))
                {
                    return site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonSelectorAfterSelect);
                }

                return projection is { IsSingleColumn: false }
                    ? site.Reject(DiagnosticDescriptors.OperatorAfterSelectNotSupported, method.Name, ReasonAggregateProjection)
                    : site.SupportedAfterSelect(AggregateKind(aggregate), isAsync);
            case Family.Queryable when method.Name is "Skip" or "Take" && parameters[1].Type.SpecialType == SpecialType.System_Int32:
                return site.SupportedAfterSelect(method.Name == "Skip" ? OperatorKind.Skip : OperatorKind.Take);
            case Family.Queryable when parameters.Length == 1
                && method.Name is "First" or "FirstOrDefault" or "Last" or "LastOrDefault" or "Single" or "SingleOrDefault" or "Any"
                    or "Count" or "LongCount":
                return site.SupportedAfterSelect(TerminalKind(method.Name));
            case Family.EntityFramework when method.Name is "ToListAsync" or "ToArrayAsync":
                return site.SupportedAfterSelect(method.Name == "ToListAsync" ? OperatorKind.ToList : OperatorKind.ToArray, isAsync: true);
            case Family.EntityFramework when parameters.Length == 2
                && method.Name is "FirstAsync" or "FirstOrDefaultAsync" or "LastAsync" or "LastOrDefaultAsync" or "SingleAsync"
                    or "SingleOrDefaultAsync" or "AnyAsync" or "CountAsync" or "LongCountAsync":
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
    /// The entity query a projection was made from, the source of the <c>Select</c> that <paramref name="source"/>
    /// comes from through other operators and local variables, with that <c>Select</c>'s projection when it translates.
    /// <see langword="null"/> when the query isn't a projection.
    /// </summary>
    private static ProjectionSource? ProjectedFrom(
        IOperation? source, SemanticModel semanticModel, INamedTypeSymbol queryableInterface, LocationInfo location, CancellationToken cancellationToken)
    {
        // A bound guards against a local initialized from itself through other locals.
        var isPaged = false;
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
                    var name = invocation.TargetMethod.Name;
                    if (family == Family.Queryable && name == "Select")
                    {
                        if (GetSource(invocation)?.Type is not { } selected || GetElementType(selected, queryableInterface) is not { } element)
                        {
                            return null;
                        }

                        var selector = invocation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 1)?.Value.Syntax;
                        while (selector is ParenthesizedExpressionSyntax parenthesized)
                        {
                            selector = parenthesized.Expression;
                        }

                        if (IsGrouping(element))
                        {
                            // A Select on groups: its projection reads the values of the groups.
                            if (GroupedFrom(GetSource(invocation), semanticModel, queryableInterface, location, cancellationToken) is not { } grouped)
                            {
                                return null;
                            }

                            var groupProjection = grouped.Group is { } group && selector is LambdaExpressionSyntax groupLambda
                                ? ProjectionTranslator.TranslateGroupSelect(groupLambda, group, semanticModel, location, cancellationToken).Projection
                                : null;
                            return new ProjectionSource(grouped.EntityType, groupProjection, isPaged);
                        }

                        // A Select after a projection reads through the members of that projection.
                        if (ProjectedFrom(GetSource(invocation), semanticModel, queryableInterface, location, cancellationToken) is { } upstream)
                        {
                            if (upstream.Aliases is not { Count: > 0 } upstreamAliases || upstream.IsPaged || selector is not LambdaExpressionSyntax aliased)
                            {
                                return new ProjectionSource(upstream.EntityType, null, isPaged);
                            }

                            var (reselected, reselectedAliases, _) = ProjectionTranslator.Translate(
                                aliased, [upstreamAliases], semanticModel, location, cancellationToken);
                            return new ProjectionSource(upstream.EntityType, reselected, isPaged, reselectedAliases, upstream.JoinCount);
                        }

                        if (selector is not LambdaExpressionSyntax lambda)
                        {
                            return new ProjectionSource(EntityDiscovery.TypeName(element), null, isPaged);
                        }

                        var (projection, aliases, _) = ProjectionTranslator.Translate(lambda, [Alias.At("", element)], semanticModel, location, cancellationToken);
                        return new ProjectionSource(EntityDiscovery.TypeName(element), projection, isPaged, aliases);
                    }

                    if (family == Family.Queryable && name == "Join" && invocation.TargetMethod.Parameters.Length == 5)
                    {
                        // A join of the entity query, or of a projection whose members the outer key and result read through.
                        var outerSource = GetSource(invocation);
                        IReadOnlyList<Alias> outer;
                        int index;
                        string entityType;
                        if (ProjectedFrom(outerSource, semanticModel, queryableInterface, location, cancellationToken) is { } projectedOuter)
                        {
                            if (projectedOuter.Aliases is not { Count: > 0 } outerAliases || projectedOuter.IsPaged)
                            {
                                return new ProjectionSource(projectedOuter.EntityType, null, isPaged);
                            }

                            (outer, index, entityType) = (outerAliases, projectedOuter.JoinCount, projectedOuter.EntityType);
                        }
                        else if (outerSource?.Type is { } outerType && GetElementType(outerType, queryableInterface) is { } outerElement)
                        {
                            (outer, index, entityType) = (Alias.At("", outerElement), 0, EntityDiscovery.TypeName(outerElement));
                        }
                        else
                        {
                            return null;
                        }

                        var (join, _) = TranslateJoin(invocation, outer, index, semanticModel, location, cancellationToken);
                        return new ProjectionSource(entityType, join?.Projection, isPaged, join?.Aliases, index + 1);
                    }

                    if (family == Family.Queryable && name == "GroupBy" && HasResultSelector(invocation.TargetMethod))
                    {
                        // GroupBy with a result selector is a Select on its groups.
                        if (GroupOf(invocation, semanticModel, queryableInterface, location, cancellationToken) is not { } grouped)
                        {
                            return null;
                        }

                        return new ProjectionSource(grouped.EntityType, grouped.Group?.ResultProjection, isPaged);
                    }

                    isPaged |= family == Family.Queryable && name is "Skip" or "Take" or "Distinct";
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

    /// <summary>
    /// The <c>GroupBy</c> a grouped query comes from, through the operators on its groups and local variables, with
    /// the translated group when it translates. <see langword="null"/> when the query isn't grouped by a query operator.
    /// </summary>
    private static GroupSource? GroupedFrom(
        IOperation? source, SemanticModel semanticModel, INamedTypeSymbol queryableInterface, LocationInfo location, CancellationToken cancellationToken)
    {
        var isFiltered = false;
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
                case IInvocationOperation invocation when GetFamily(invocation.TargetMethod.ContainingType) is Family.Queryable or Family.EntityFramework:
                    if (invocation.TargetMethod.Name == "GroupBy")
                    {
                        return GroupOf(invocation, semanticModel, queryableInterface, location, cancellationToken) is { } grouped
                            ? grouped with { IsFiltered = isFiltered }
                            : null;
                    }

                    isFiltered |= invocation.TargetMethod.Name is "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending";
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

    // A GroupBy call translated against the entity it groups. A GroupBy of a projection is reported where it is called.
    private static GroupSource? GroupOf(
        IInvocationOperation groupBy, SemanticModel semanticModel, INamedTypeSymbol queryableInterface, LocationInfo location, CancellationToken cancellationToken)
    {
        if (GetSource(groupBy) is not { Type: { } sourceType } source || GetElementType(sourceType, queryableInterface) is not { } entity)
        {
            return null;
        }

        if (ProjectedFrom(source, semanticModel, queryableInterface, location, cancellationToken) is { } projected)
        {
            return new GroupSource(projected.EntityType, Group: null);
        }

        var (group, _) = ProjectionTranslator.TranslateGroupBy(groupBy, entity, semanticModel, location, cancellationToken);
        return new GroupSource(EntityDiscovery.TypeName(entity), group);
    }

    // The operators that can follow GroupBy on its groups; anything else is reported.
    private static CallSite? ClassifyGrouped(SiteBuilder site, IMethodSymbol method, Family family, GroupSource grouped)
    {
        if (grouped.Group is not { } group)
        {
            // The GroupBy itself is reported.
            return site.Silent();
        }

        var parameters = method.Parameters;
        var name = family == Family.EntityFramework && method.Name.EndsWith("Async", StringComparison.Ordinal)
            ? method.Name.Substring(0, method.Name.Length - "Async".Length)
            : method.Name;
        var isAsync = family == Family.EntityFramework;
        switch (family)
        {
            case Family.Queryable when name == "Where" && IsPredicate(parameters[1].Type):
                return site.OnGroups(OperatorKind.Where, group, lambdaOrdinal: 1);
            case Family.Queryable when name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" && parameters.Length == 2:
                return site.OnGroups(OrderKind(name), group, lambdaOrdinal: 1);
            case Family.Queryable when name == "Select" && IsSelector(parameters[1].Type):
                return site.OnGroups(OperatorKind.Select, group, lambdaOrdinal: 1);
            case Family.Queryable or Family.EntityFramework when name is "Count" or "LongCount" or "Any":
                var predicateArguments = isAsync ? 3 : 2;
                if (parameters.Length == predicateArguments && IsPredicate(parameters[1].Type))
                {
                    return site.OnGroups(TerminalKind(name), group, lambdaOrdinal: 1, isAsync);
                }

                return parameters.Length == predicateArguments - 1
                    ? site.OnGroups(TerminalKind(name), group, lambdaOrdinal: null, isAsync)
                    : site.Overload(ReasonDefaultValue);
            case Family.EntityFramework when name is "ToList" or "ToArray":
            case Family.Enumerable when parameters.Length == 1 && name is "ToList" or "ToArray":
                return grouped.IsFiltered
                    ? site.Reject(DiagnosticDescriptors.OperatorOnGroupsNotSupported, method.Name, ReasonGroupsAfterFilter)
                    : site.OnGroups(name == "ToList" ? OperatorKind.ToList : OperatorKind.ToArray, group, lambdaOrdinal: null, isAsync);
            case Family.Enumerable or Family.DbSet:
                // Enumerating the groups runs the query.
                return null;
            default:
                return site.Reject(DiagnosticDescriptors.OperatorOnGroupsNotSupported, method.Name, ReasonOnGroups);
        }
    }

    // IGrouping<TKey, TElement>, the element of a grouped query.
    private static bool IsGrouping(ITypeSymbol type) => type.OriginalDefinition.ToDisplayString() == GroupingType;

    // GroupBy's result selector is an Expression<Func<TKey, IEnumerable<TElement>, TResult>>.
    private static bool HasResultSelector(IMethodSymbol method)
        => method.Parameters.Skip(1).Any(static p => p.Type is INamedTypeSymbol { TypeArguments.Length: 1 } expression
            && expression.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 3 });

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

    private static string? AggregateName(string name) => name switch
    {
        "Sum" or "SumAsync" => "Sum",
        "Average" or "AverageAsync" => "Average",
        "Min" or "MinAsync" => "Min",
        "Max" or "MaxAsync" => "Max",
        _ => null,
    };

    private static OperatorKind AggregateKind(string name) => name switch
    {
        "Sum" => OperatorKind.Sum,
        "Average" => OperatorKind.Average,
        "Min" => OperatorKind.Min,
        _ => OperatorKind.Max,
    };

    // The runtime's AggregateValueType: the numeric type of a Sum or Average overload; Min and Max read the column as it is.
    internal static string AggregateValueType(OperatorKind kind, ITypeSymbol type)
    {
        if (kind is OperatorKind.Min or OperatorKind.Max)
        {
            return "Column";
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return type.SpecialType switch
        {
            SpecialType.System_Int32 => "Int32",
            SpecialType.System_Int64 => "Int64",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_Double => "Double",
            SpecialType.System_Single => "Single",
            _ => "Column",
        };
    }

    // An async terminal's result is the task's type argument.
    private static ITypeSymbol ResultType(IMethodSymbol method, bool isAsync)
        => isAsync && method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } task ? task.TypeArguments[0] : method.ReturnType;

    // Expression<Func<...>>, as opposed to an IEqualityComparer.
    private static bool IsLambdaParameter(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeArguments.Length: 1 } expression && expression.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Delegate };

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
        "Last" => OperatorKind.Last,
        "LastOrDefault" => OperatorKind.LastOrDefault,
        "Single" => OperatorKind.Single,
        "SingleOrDefault" => OperatorKind.SingleOrDefault,
        "Any" => OperatorKind.Any,
        "Count" => OperatorKind.Count,
        _ => OperatorKind.LongCount,
    };

    private sealed partial class SiteBuilder(
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
        {
            var declared = (operation.TargetMethod.ReducedFrom ?? operation.TargetMethod).OriginalDefinition;
            var aggregate = kind is OperatorKind.Sum or OperatorKind.Average or OperatorKind.Min or OperatorKind.Max
                ? new AggregateModel(
                    AggregateValueType(kind, elementSymbol),
                    PropertyPath: null,
                    ResultType(declared, isAsync).ToDisplayString(SignatureFormat))
                : null;
            return new(
                _elementType,
                location,
                kind,
                isAsync,
                HasPredicate: false,
                Diagnostic: null,
                Interception: semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Signature: GetGenericSignature(operation.TargetMethod),
                ProjectedFrom: projectedFrom,
                Aggregate: aggregate);
        }

        /// <summary>
        /// <c>Find</c> or <c>FindAsync</c>, which are methods of the set itself: the interceptor takes the set as its
        /// <see langword="this"/> parameter, followed by the method's parameters.
        /// </summary>
        public CallSite Find(bool isAsync)
        {
            var method = operation.TargetMethod;
            var set = new ParameterSignature(method.ContainingType.ToDisplayString(SignatureFormat), "set");
            var parameters = new[] { set }
                .Concat(method.Parameters.Select(static p => new ParameterSignature(p.Type.ToDisplayString(SignatureFormat), p.Name)))
                .ToEquatableArray();
            return new(
                _elementType,
                location,
                OperatorKind.Find,
                isAsync,
                HasPredicate: false,
                Diagnostic: null,
                Interception: semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Signature: new InterceptorSignature(
                    method.Name, EntityDiscovery.TypeName(method.ContainingType), method.ReturnType.ToDisplayString(SignatureFormat), parameters));
        }

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
            AggregateModel? aggregate = null;
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
                    var (translated, translationDiagnostic) = PredicateTranslator.Translate(
                        lambda, semanticModel, cancellationToken, allowNavigations: true);
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
                else if (lambda is not null && kind is OperatorKind.Sum or OperatorKind.Average or OperatorKind.Min or OperatorKind.Max)
                {
                    var path = ProjectionTranslator.TranslatePropertyPath(lambda, elementSymbol, semanticModel, cancellationToken);
                    if (path is null)
                    {
                        var diagnostic = DiagnosticInfo.Create(
                            DiagnosticDescriptors.ProjectionNotSupported, LocationInfo.From(lambda.GetLocation()), lambda.ToString(), ReasonAggregateSelector);
                        return new CallSite(_elementType, location, Operator: null, isAsync, hasPredicate, diagnostic);
                    }

                    var selected = ((INamedTypeSymbol)((INamedTypeSymbol)operation.TargetMethod.Parameters[1].Type).TypeArguments[0]).TypeArguments[1];
                    aggregate = new AggregateModel(
                        AggregateValueType(kind, selected), path, ResultType(operation.TargetMethod, isAsync).ToDisplayString(SignatureFormat));
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
                projection,
                Aggregate: aggregate);
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

        /// <summary>A <c>GroupBy</c> of the entity query, with its key, element and result selector translated.</summary>
        public CallSite GroupBy()
        {
            foreach (var argument in operation.Arguments.Where(static a => a.Parameter?.Ordinal > 0))
            {
                if (RequireLambda(argument.Parameter!.Ordinal).Diagnostic is { } lambdaDiagnostic)
                {
                    return Failed(lambdaDiagnostic);
                }
            }

            var (group, diagnostic) = ProjectionTranslator.TranslateGroupBy(operation, elementSymbol, semanticModel, location, cancellationToken);
            if (diagnostic is not null || group is null)
            {
                return diagnostic is null ? Unsupported() : Failed(diagnostic);
            }

            var signature = GetGenericSignature(operation.TargetMethod);
            var model = group.Model;
            var hasElementSelector = signature.Parameters.Count == 4 || (signature.Parameters.Count == 3 && model.Result is null);
            model = model with
            {
                ElementParameter = hasElementSelector && model.ElementCaptureCount > 0 ? signature.Parameters[2].Name : null,
                Result = model.Result is { } result ? result with { CaptureParameter = signature.Parameters[signature.Parameters.Count - 1].Name } : null,
            };

            return Group(OperatorKind.GroupBy, new GroupCallModel(GroupCallKind.GroupBy, GroupBy: model), isAsync: false, hasPredicate: false);
        }

        /// <summary>An operator on the groups of a <c>GroupBy</c>.</summary>
        public CallSite OnGroups(OperatorKind kind, GroupShape group, int? lambdaOrdinal, bool isAsync = false)
        {
            GroupCallModel call;
            if (lambdaOrdinal is not { } ordinal)
            {
                call = new GroupCallModel(GroupCallKind.Terminal);
            }
            else
            {
                var (lambda, lambdaDiagnostic) = RequireLambda(ordinal);
                if (lambda is null)
                {
                    return lambdaDiagnostic is null ? Unsupported() : Failed(lambdaDiagnostic);
                }

                GroupCallModel? translated;
                DiagnosticInfo? diagnostic;
                if (kind == OperatorKind.Select)
                {
                    var (projection, selectDiagnostic) = ProjectionTranslator.TranslateGroupSelect(lambda, group, semanticModel, location, cancellationToken);
                    (translated, diagnostic) = (projection is null ? null : ProjectionTranslator.SelectCall(projection), selectDiagnostic);
                }
                else
                {
                    (translated, diagnostic) = IsOrdering(kind)
                        ? ProjectionTranslator.TranslateGroupOrder(lambda, group, semanticModel, location, cancellationToken)
                        : ProjectionTranslator.TranslateGroupPredicate(lambda, group, semanticModel, location, cancellationToken);
                }

                if (diagnostic is not null)
                {
                    return Failed(diagnostic);
                }

                call = translated! with { CaptureParameter = GetGenericSignature(operation.TargetMethod).Parameters[ordinal].Name };
            }

            return Group(kind, call, isAsync, hasPredicate: lambdaOrdinal is not null && kind is not (OperatorKind.Where or OperatorKind.Select)
                && !IsOrdering(kind));
        }

        /// <summary>A <c>Where</c> or an ordering after a <c>Select</c> on groups, over the members that are group values.</summary>
        public CallSite AfterGroupSelect(OperatorKind kind, ProjectionModel projection)
        {
            var (lambda, lambdaDiagnostic) = RequireLambda(1);
            if (lambda is null)
            {
                return lambdaDiagnostic is null ? Unsupported() : Failed(lambdaDiagnostic);
            }

            var (call, diagnostic) = kind == OperatorKind.Where
                ? ProjectionTranslator.TranslateProjectedPredicate(lambda, projection, elementSymbol, semanticModel, location, cancellationToken)
                : ProjectionTranslator.TranslateProjectedOrder(lambda, projection, elementSymbol, semanticModel, location, cancellationToken);
            if (diagnostic is not null)
            {
                return Failed(diagnostic);
            }

            return Group(kind, call! with { CaptureParameter = GetGenericSignature(operation.TargetMethod).Parameters[1].Name }, isAsync: false, hasPredicate: false);
        }

        // An intercepted GroupBy or operator on its groups. Its interceptor is generic, since a key can be anonymous.
        private CallSite Group(OperatorKind kind, GroupCallModel call, bool isAsync, bool hasPredicate)
        {
            var entity = kind == OperatorKind.GroupBy ? EntityShapeBuilder.Build(elementSymbol, semanticModel.Compilation, location) : null;

            // The caller builds the lambdas' decimal operators by reflection; the interceptor keeps them for it.
            var usesDecimalOperators = operation.Arguments
                .Select(static a => a.Value.Syntax)
                .OfType<ExpressionSyntax>()
                .Select(PredicateTranslator.StripParentheses)
                .OfType<LambdaExpressionSyntax>()
                .Any(lambda => DecimalOperators.AreUsedIn(lambda, semanticModel, cancellationToken));
            return new CallSite(
                _elementType,
                location,
                kind,
                isAsync,
                hasPredicate,
                Diagnostic: null,
                entity,
                semanticModel.GetInterceptableLocation(invocation, cancellationToken)?.GetInterceptsLocationAttributeSyntax(),
                Signature: GetGenericSignature(operation.TargetMethod),
                UsesDecimalOperators: usesDecimalOperators,
                ProjectedFrom: projectedFrom,
                Group: call);
        }

        /// <summary>An operator whose query is reported elsewhere, such as an operator on the groups of a reported GroupBy.</summary>
        public CallSite Silent() => new(_elementType, location, Operator: null, IsAsync: false, HasPredicate: false, Diagnostic: null, ProjectedFrom: projectedFrom);

        private CallSite Failed(DiagnosticInfo diagnostic)
            => new(_elementType, location, Operator: null, IsAsync: false, HasPredicate: false, diagnostic, ProjectedFrom: projectedFrom);

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

/// <summary>
/// The entity query a projection was made from, its <c>Select</c>'s projection when it translates, and whether
/// <c>Skip</c>, <c>Take</c> or <c>Distinct</c> came after the <c>Select</c>.
/// </summary>
/// <param name="EntityType">The queried entity of the chain.</param>
/// <param name="Projection">The projection, when it translates.</param>
/// <param name="IsPaged">Whether <c>Skip</c>, <c>Take</c> or <c>Distinct</c> came after it.</param>
/// <param name="Aliases">What the projection's members stand for, which the operators after it read through.</param>
/// <param name="JoinCount">How many joins the chain has made, which numbers the next one's join path.</param>
internal readonly record struct ProjectionSource(
    string EntityType, ProjectionModel? Projection, bool IsPaged = false, IReadOnlyList<Alias>? Aliases = null, int JoinCount = 0);

/// <summary>
/// The entity query a grouped query was grouped from, the translated <c>GroupBy</c> when it translates, and whether a
/// <c>Where</c> or an ordering came after it.
/// </summary>
internal readonly record struct GroupSource(string EntityType, GroupShape? Group, bool IsFiltered = false);
