using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Filters;

/// <summary>A query filter to apply, with its values read from the current context.</summary>
internal sealed record ResolvedQueryFilter(string? Key, PredicateTemplate Predicate, IReadOnlyList<object?> Values);
