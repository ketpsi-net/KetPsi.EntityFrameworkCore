namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>The supported operators, each of which gets an interceptor.</summary>
internal enum OperatorKind
{
    Where,
    WhereIf,
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,
    Skip,
    Take,
    AsNoTracking,
    AsNoTrackingWithIdentityResolution,
    AsTracking,
    IgnoreQueryFilters,
    ToList,
    ToArray,
    First,
    FirstOrDefault,
    Single,
    SingleOrDefault,
    Count,
    LongCount,
    Any,
}
