using System.Globalization;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Filters;

/// <summary>
/// Matches the query filters declared in the model with their build-time translations. The model stays the
/// source of truth for which filters exist; a model filter without a translation is an error, never skipped.
/// </summary>
internal static class QueryFilterResolver
{
    /// <exception cref="InvalidOperationException">
    /// A filter that applies has no registered translation, or its value reader returned the wrong number of values.
    /// </exception>
    public static IReadOnlyList<ResolvedQueryFilter> Resolve(DbContext context, BoundEntity entity, IgnoredQueryFilters ignored)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(ignored);

        if (entity.FilterKeys.Count == 0 || ignored.All)
        {
            return [];
        }

        var contextType = context.GetType();
        var filters = new List<ResolvedQueryFilter>(entity.FilterKeys.Count);
        foreach (var key in entity.FilterKeys)
        {
            if (ignored.IsIgnored(key))
            {
                continue;
            }

            var registration = QueryFilters.Find(contextType, entity.ClrType, key)
                ?? throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    ExceptionMessages.QueryFilterNotTranslated,
                    entity.ClrType,
                    QueryFilters.DisplayKey(key),
                    contextType));

            var values = registration.Values(context);
            if (values is null || values.Length != registration.Predicate.ValueCount)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    ExceptionMessages.QueryFilterValueCountMismatch,
                    entity.ClrType,
                    QueryFilters.DisplayKey(key),
                    registration.Predicate.ValueCount,
                    values?.Length ?? 0));
            }

            filters.Add(new ResolvedQueryFilter(key, registration.Predicate, values));
        }

        return filters;
    }
}
