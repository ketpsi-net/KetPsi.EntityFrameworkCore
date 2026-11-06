using System.Collections;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Returns the groups of a <c>GroupBy</c> themselves, as EF Core does: the statement reads each row's key and element
/// ordered by the key, and consecutive rows with the same key form a group. Elements that are entities are tracked,
/// identity-resolved and given their includes like projected entities. The order of the elements within a group is
/// the database's, which EF Core doesn't define either.
/// </summary>
internal static class GroupRunner
{
    public static IEnumerable<IGrouping<TKey, TElement>> Enumerate<TKey, TElement>(GroupedQueryable<TKey, TElement> query)
        => Group(ProjectionRunner.Enumerate(Rows(query)));

    public static IAsyncEnumerable<IGrouping<TKey, TElement>> EnumerateAsync<TKey, TElement>(
        GroupedQueryable<TKey, TElement> query, CancellationToken cancellationToken)
        => GroupAsync(ProjectionRunner.EnumerateAsync(Rows(query), cancellationToken), cancellationToken);

    public static List<IGrouping<TKey, TElement>> ToList<TKey, TElement>(GroupedQueryable<TKey, TElement> query) => [.. Enumerate(query)];

    public static async Task<List<IGrouping<TKey, TElement>>> ToListAsync<TKey, TElement>(
        GroupedQueryable<TKey, TElement> query, CancellationToken cancellationToken)
    {
        var groups = new List<IGrouping<TKey, TElement>>();
        await foreach (var group in EnumerateAsync(query, cancellationToken).ConfigureAwait(false))
        {
            groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// The query that reads each row as its key and element: a projection of the key paths, then the element's
    /// columns and entities, which the statement orders by the key.
    /// </summary>
    private static ProjectedQueryable<KeyValuePair<TKey, TElement>> Rows<TKey, TElement>(GroupedQueryable<TKey, TElement> query)
    {
        var group = query.Group;
        var projection = new ProjectionNode(query.Tail, [.. group.KeyPaths, .. group.ElementPropertyNames], group.ElementEntityPaths);
        var keyCount = group.KeyPaths.Count;
        var keyReader = query.KeyReader;
        var elementReader = query.ElementReader;
        return new ProjectedQueryable<KeyValuePair<TKey, TElement>>(
            query.Context,
            projection,
            (reader, ordinals, entities) => new(keyReader(reader, ordinals[..keyCount], []), elementReader(reader, ordinals[keyCount..], entities)));
    }

    private static IEnumerable<IGrouping<TKey, TElement>> Group<TKey, TElement>(IEnumerable<KeyValuePair<TKey, TElement>> rows)
    {
        QueryGrouping<TKey, TElement>? current = null;
        foreach (var row in rows)
        {
            if (Starts(current, row.Key))
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = new QueryGrouping<TKey, TElement>(row.Key);
            }

            current!.Add(row.Value);
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    private static async IAsyncEnumerable<IGrouping<TKey, TElement>> GroupAsync<TKey, TElement>(
        IAsyncEnumerable<KeyValuePair<TKey, TElement>> rows, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        QueryGrouping<TKey, TElement>? current = null;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (Starts(current, row.Key))
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = new QueryGrouping<TKey, TElement>(row.Key);
            }

            current!.Add(row.Value);
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    // A new group starts where the key changes, compared as C# compares it, as in EF Core.
    private static bool Starts<TKey, TElement>(QueryGrouping<TKey, TElement>? current, TKey key)
        => current is null || !EqualityComparer<TKey>.Default.Equals(current.Key, key);

    /// <summary>A group a query returned: its key and its elements, in the order they were read.</summary>
    private sealed class QueryGrouping<TKey, TElement>(TKey key) : IGrouping<TKey, TElement>
    {
        private readonly List<TElement> _elements = [];

        public TKey Key { get; } = key;

        public void Add(TElement element) => _elements.Add(element);

        public IEnumerator<TElement> GetEnumerator() => _elements.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
