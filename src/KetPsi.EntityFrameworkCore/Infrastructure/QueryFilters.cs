using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Build-time translations of the query filters configured with <c>HasQueryFilter</c>. Code emitted by
/// KetPsi.EntityFrameworkCore.Generator registers them; applications don't call this.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class QueryFilters
{
    private static readonly ConcurrentDictionary<FilterId, QueryFilterRegistration> Registrations = new();

    /// <summary>Registers the translation of one query filter.</summary>
    /// <param name="contextType">
    /// The <see cref="DbContext"/> type whose model declares the filter; <c>typeof(DbContext)</c> applies it to every context.
    /// </param>
    /// <param name="entityType">The filtered entity type.</param>
    /// <param name="filterKey">The filter's name, or <see langword="null"/> for an unnamed filter.</param>
    /// <param name="predicate">The filter translated at build time.</param>
    /// <param name="values">Reads the filter's value slots from the context the query runs on.</param>
    /// <exception cref="InvalidOperationException">A different translation is already registered for the filter.</exception>
    public static void Register(
        Type contextType,
        Type entityType,
        string? filterKey,
        PredicateTemplate predicate,
        Func<DbContext, object?[]> values)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(values);
        if (filterKey is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(filterKey);
        }

        if (!typeof(DbContext).IsAssignableFrom(contextType))
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, ExceptionMessages.NotADbContextType, contextType),
                nameof(contextType));
        }

        var registration = new QueryFilterRegistration(predicate, values);
        var registered = Registrations.GetOrAdd(new FilterId(contextType, entityType, filterKey), registration);
        if (!ReferenceEquals(registered.Predicate, predicate))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.QueryFilterAlreadyRegistered, entityType, DisplayKey(filterKey), contextType));
        }
    }

    /// <summary>Finds the translation for a filter, looking from <paramref name="contextType"/> up through its base types.</summary>
    internal static QueryFilterRegistration? Find(Type contextType, Type entityType, string? filterKey)
    {
        for (var type = contextType; type is not null && type != typeof(object); type = type.BaseType)
        {
            if (Registrations.TryGetValue(new FilterId(type, entityType, filterKey), out var registration))
            {
                return registration;
            }
        }

        return null;
    }

    internal static string DisplayKey(string? filterKey) => filterKey ?? "(unnamed)";

    private readonly record struct FilterId(Type ContextType, Type EntityType, string? FilterKey);
}

/// <summary>A registered query filter translation.</summary>
internal sealed record QueryFilterRegistration(PredicateTemplate Predicate, Func<DbContext, object?[]> Values);
