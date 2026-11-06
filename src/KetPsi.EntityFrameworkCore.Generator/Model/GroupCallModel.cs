namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>What an interceptor of a <c>GroupBy</c>, or of an operator on its groups, passes to the runtime.</summary>
/// <param name="Kind">Which call it is.</param>
/// <param name="GroupBy">For <see cref="GroupCallKind.GroupBy"/>, the key and element.</param>
/// <param name="Values">
/// C# that creates each <c>GroupValue</c> the call reads: the operands of a predicate, the value of an ordering, or the
/// values of a <c>Select</c>, in the order of their ordinals.
/// </param>
/// <param name="PredicateValues">C# that reads each value slot of the predicate and the values' count predicates.</param>
/// <param name="TemplateCode">For a predicate, C# that constructs its template over the operands.</param>
/// <param name="ReaderCode">For a <c>Select</c> or a result selector, the reader lambda over the values.</param>
/// <param name="CaptureCount">How many captured values the code reads from <c>captures</c>.</param>
/// <param name="CaptureParameter">The interceptor parameter whose expression tree holds the captured values.</param>
internal sealed record GroupCallModel(
    GroupCallKind Kind,
    GroupByModel? GroupBy = null,
    EquatableArray<string> Values = default,
    EquatableArray<string> PredicateValues = default,
    string? TemplateCode = null,
    string? ReaderCode = null,
    int CaptureCount = 0,
    string? CaptureParameter = null);

internal enum GroupCallKind
{
    /// <summary><c>GroupBy</c>, which may also select with a result selector.</summary>
    GroupBy,

    /// <summary><c>Where</c> on the groups, or the predicate of a terminal on them.</summary>
    Where,

    /// <summary>An ordering on the groups.</summary>
    Order,

    /// <summary><c>Select</c> on the groups.</summary>
    Select,

    /// <summary>A terminal on the groups: <c>Count</c>, <c>LongCount</c>, <c>Any</c>, <c>ToList</c> or <c>ToArray</c>.</summary>
    Terminal,

    /// <summary><c>Where</c> after a <c>Select</c> on the groups.</summary>
    ProjectedWhere,

    /// <summary>An ordering after a <c>Select</c> on the groups.</summary>
    ProjectedOrder,
}

/// <summary>The key and element of a <c>GroupBy</c>, as the runtime's <c>SqlGrouping.GroupBy</c> takes them.</summary>
/// <param name="EntityType">The fully qualified grouped entity.</param>
/// <param name="KeyPaths">The property paths of the key, one per part.</param>
/// <param name="KeyReaderCode">The reader lambda that builds the key from the key columns.</param>
/// <param name="ElementPaths">The property paths the element reader reads.</param>
/// <param name="ElementEntityPaths">The navigation paths of the entities the element reader uses.</param>
/// <param name="ElementReaderCode">The reader lambda that builds the element.</param>
/// <param name="ElementCaptureCount">How many captured values the element reader reads from <c>captures</c>.</param>
/// <param name="Entities">The entities the element reads, which need a reader and navigation accessors.</param>
/// <param name="Result">For a result selector, the values and reader of the <c>Select</c> it stands for.</param>
/// <param name="ElementParameter">For an element selector, the interceptor parameter whose tree holds its captured values.</param>
internal sealed record GroupByModel(
    string EntityType,
    EquatableArray<string> KeyPaths,
    string KeyReaderCode,
    EquatableArray<string> ElementPaths,
    EquatableArray<string> ElementEntityPaths,
    string ElementReaderCode,
    int ElementCaptureCount,
    EquatableArray<EntityShape> Entities,
    GroupCallModel? Result = null,
    string? ElementParameter = null);
