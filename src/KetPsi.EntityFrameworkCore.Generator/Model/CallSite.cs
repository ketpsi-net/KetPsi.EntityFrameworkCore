namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>
/// A call to a query operator, classified without knowing the entity types yet: the pipeline keeps it only
/// when <see cref="ElementType"/> turns out to be an entity.
/// </summary>
/// <param name="ElementType">The fully qualified element type of the query the operator is called on.</param>
/// <param name="Location">The operator name, where diagnostics point.</param>
/// <param name="Operator">The supported operator, or <see langword="null"/> when <see cref="Diagnostic"/> rejects the call.</param>
/// <param name="IsAsync">Whether the operator is an EF Core async terminal.</param>
/// <param name="HasPredicate">Whether a terminal takes a predicate, such as <c>First(x =&gt; ...)</c>.</param>
/// <param name="Diagnostic">Why the call is not supported, or <see langword="null"/>.</param>
/// <param name="Entity">For supported operators on a class, how to read the element type.</param>
/// <param name="Interception">For supported operators, the <c>InterceptsLocation</c> attribute that targets the call.</param>
/// <param name="Predicate">For operators that take a predicate, its translation.</param>
/// <param name="Signature">For supported operators, the method the interceptor replaces.</param>
/// <param name="OrderProperty">For ordering operators, the entity property the key selector reads.</param>
/// <param name="Include">For <c>Include</c> and <c>ThenInclude</c>, the included navigation.</param>
/// <param name="UsesDecimalOperators">
/// Whether the call's lambda uses a decimal operator, whose expression tree needs decimal's methods kept for
/// reflection under Native AOT.
/// </param>
/// <param name="Projection">For <c>Select</c>, the translated selector.</param>
/// <param name="Aggregate">For <c>Sum</c>, <c>Average</c>, <c>Min</c> and <c>Max</c>, what to compute.</param>
/// <param name="ProjectedFrom">
/// For an operator on a projected query, the fully qualified type of the entity query the <c>Select</c> was called on;
/// the site is kept when that type is an entity.
/// </param>
internal sealed record CallSite(
    string ElementType,
    LocationInfo Location,
    OperatorKind? Operator,
    bool IsAsync,
    bool HasPredicate,
    DiagnosticInfo? Diagnostic,
    EntityShape? Entity = null,
    string? Interception = null,
    PredicateModel? Predicate = null,
    InterceptorSignature? Signature = null,
    string? OrderProperty = null,
    IncludeModel? Include = null,
    bool UsesDecimalOperators = false,
    ProjectionModel? Projection = null,
    string? ProjectedFrom = null,
    AggregateModel? Aggregate = null);

/// <summary>An aggregate, as the runtime's <c>AggregateFunction</c> and <c>AggregateValueType</c> name it.</summary>
/// <param name="ValueType">The overload's numeric type, or <c>Column</c> for <c>Min</c> and <c>Max</c>.</param>
/// <param name="PropertyPath">The property path the selector reads, or <see langword="null"/> after a projection.</param>
/// <param name="ResultType">The fully qualified result type, without the async terminal's task.</param>
internal sealed record AggregateModel(string ValueType, string? PropertyPath, string ResultType);

/// <summary>An included navigation, as an <c>Include</c> or <c>ThenInclude</c> lambda reads it.</summary>
/// <param name="Navigation">The navigation name, such as <c>Posts</c>.</param>
/// <param name="PropertyTypeName">The fully qualified type of the navigation property, the operator's <c>TProperty</c>.</param>
/// <param name="Source">The entity that declares the navigation.</param>
/// <param name="Target">The included entity: the navigation's type, or a collection's element type.</param>
internal sealed record IncludeModel(string Navigation, string PropertyTypeName, EntityShape Source, EntityShape Target);
