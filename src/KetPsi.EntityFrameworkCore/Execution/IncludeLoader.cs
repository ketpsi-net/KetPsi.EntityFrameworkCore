using System.Data.Common;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// Runs a query with included navigations: the statement that reads the entities with their joined references,
/// then one statement per included collection with the keys of the parents already read, and connects the
/// entities the way EF Core's own queries do.
/// </summary>
/// <remarks>
/// <para>
/// Tracking queries attach every entity, which runs EF Core's navigation fixup, and mark each included navigation
/// as loaded. An entity the context already tracks is returned as it is, without being refreshed.
/// </para>
/// <para>
/// No-tracking queries create one instance per row and set the navigations, inverse navigations included, with
/// the generated <see cref="INavigationAccessor"/>s; a reference shared by several rows is a separate instance in
/// each. With identity resolution, every entity is one instance per key across the whole query.
/// </para>
/// </remarks>
internal sealed class IncludeLoader
{
    private readonly DbContext _context;
    private readonly ISqlDialect _dialect;
    private readonly QueryTrackingBehavior _tracking;
    private readonly Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> _filtersFor;
    private readonly Func<Type, BoundEntity> _bindEntity;
    private readonly Dictionary<Type, Dictionary<object?[], object>> _identities = [];
    private readonly HashSet<(object Owner, object Item)> _addedToCollections = new(PairComparer.Instance);

    public IncludeLoader(
        DbContext context,
        ISqlDialect dialect,
        QueryTrackingBehavior tracking,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> filtersFor,
        Func<Type, BoundEntity> bindEntity)
    {
        _context = context;
        _dialect = dialect;
        _tracking = tracking;
        _filtersFor = filtersFor;
        _bindEntity = bindEntity;
    }

    private bool IsTracking => _tracking == QueryTrackingBehavior.TrackAll;

    /// <summary>Runs <paramref name="statement"/> and its collection loads, and returns the read entities in row order.</summary>
    public List<object> Load(IncludeStatement statement)
        => LoadAsync(statement, isAsync: false, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>Runs <paramref name="statement"/> and its collection loads, and returns the read entities in row order.</summary>
    public ValueTask<List<object>> LoadAsync(IncludeStatement statement, CancellationToken cancellationToken)
        => LoadAsync(statement, isAsync: true, cancellationToken);

    private async ValueTask<List<object>> LoadAsync(IncludeStatement statement, bool isAsync, CancellationToken cancellationToken)
    {
        var plan = new StatementPlan(statement);
        var roots = new List<object>();
        await ReadAsync(statement.Statement, row =>
        {
            var entities = ReadRow(plan, row);
            roots.Add(entities[0]!);
            plan.CollectParents(row, entities);
        }, isAsync, cancellationToken).ConfigureAwait(false);

        await LoadCollectionsAsync(plan, isAsync, cancellationToken).ConfigureAwait(false);
        return roots;
    }

    private async ValueTask LoadCollectionsAsync(StatementPlan plan, bool isAsync, CancellationToken cancellationToken)
    {
        foreach (var load in plan.Loads)
        {
            if (load.Parents.Count == 0)
            {
                continue;
            }

            var navigation = load.Branch.Navigation;
            var keys = new Dictionary<object?[], List<object>>(KeyReader.Comparer);
            foreach (var (parent, key) in load.Parents)
            {
                if (!IsTracking)
                {
                    // EF Core gives every parent of an included collection a collection, empty or not.
                    Accessor(parent, navigation).EnsureCollection(parent);
                }

                if (key.Any(static value => value is null))
                {
                    continue;
                }

                if (!keys.TryGetValue(key, out var parents))
                {
                    keys.Add(key, parents = []);
                }

                parents.Add(parent);
            }

            if (keys.Count > 0)
            {
                var statement = SqlBuilder.BuildCollection(load.Branch, [.. keys.Keys], _dialect, _filtersFor);
                var childPlan = new StatementPlan(statement);
                var foreignKey = new KeyReader(load.Branch.Target.Entity, ForeignKeyProperties(load.Branch), offset: 0);
                await ReadAsync(statement.Statement, row =>
                {
                    var entities = ReadRow(childPlan, row);
                    var child = entities[0]!;
                    childPlan.CollectParents(row, entities);

                    // SQL Server can match string keys that differ in case; EF Core's fixup compares them exactly.
                    if (keys.TryGetValue(foreignKey.Read(row), out var parents))
                    {
                        foreach (var parent in parents)
                        {
                            AddChild(parent, navigation, child);
                        }
                    }
                }, isAsync, cancellationToken).ConfigureAwait(false);

                await LoadCollectionsAsync(childPlan, isAsync, cancellationToken).ConfigureAwait(false);
            }

            if (IsTracking)
            {
                foreach (var (parent, _) in load.Parents)
                {
                    // Fixup filled the collections that have children; the others are created empty, as EF Core does.
                    Accessor(parent, navigation).EnsureCollection(parent);
                    MarkLoaded(parent, navigation);
                }
            }
        }
    }

    private IReadOnlyList<BoundProperty> ForeignKeyProperties(IncludeBranch branch)
    {
        var target = branch.Target.Entity;
        return [.. branch.Navigation.TargetKey.Select(target.GetProperty)];
    }

    private object?[] ReadRow(StatementPlan plan, DbDataReader row)
    {
        var entities = new object?[plan.Segments.Count];
        for (var i = 0; i < entities.Length; i++)
        {
            var segment = plan.Segments[i];
            if (segment.Parent is not { } parentIndex)
            {
                entities[i] = Resolve(segment, row);
                continue;
            }

            if (entities[parentIndex] is not { } parent)
            {
                continue;
            }

            var target = segment.IsPresent(row) ? Resolve(segment, row) : null;
            entities[i] = target;
            LinkReference(parent, segment.Navigation!, target);
        }

        return entities;
    }

    private object Resolve(SegmentReader segment, DbDataReader row)
    {
        if (segment.Key is not { } keyReader)
        {
            return segment.Materialize(row);
        }

        var key = keyReader.Read(row);
        if (IsTracking)
        {
            if (segment.Operations.FindTracked(_context, key) is { } existing)
            {
                return existing;
            }

            var entity = segment.Materialize(row);
            var entry = _context.Entry(entity);

            // Shadow values must be in place before attaching, so EF Core snapshots them as original values.
            foreach (var (name, ordinal, clrType) in segment.ShadowProperties)
            {
                entry.Property(name).CurrentValue = KeyReader.ToClrValue(row.GetValue(ordinal), clrType);
            }

            entry.State = EntityState.Unchanged;
            return entity;
        }

        if (_tracking == QueryTrackingBehavior.NoTrackingWithIdentityResolution)
        {
            var type = segment.Entity.ClrType;
            if (!_identities.TryGetValue(type, out var identities))
            {
                _identities.Add(type, identities = new Dictionary<object?[], object>(KeyReader.Comparer));
            }

            if (!identities.TryGetValue(key, out var existing))
            {
                identities.Add(key, existing = segment.Materialize(row));
            }

            return existing;
        }

        return segment.Materialize(row);
    }

    private void LinkReference(object parent, BoundNavigation navigation, object? target)
    {
        if (IsTracking)
        {
            // Attaching ran EF Core's fixup; what remains is recording that the navigation was loaded.
            MarkLoaded(parent, navigation);
            return;
        }

        if (target is null)
        {
            return;
        }

        Accessor(parent, navigation).SetReference(parent, target);
        LinkInverse(target, navigation, parent);
    }

    private void AddChild(object parent, BoundNavigation navigation, object child)
    {
        if (IsTracking)
        {
            return;
        }

        if (_addedToCollections.Add((parent, child)))
        {
            Accessor(parent, navigation).AddToCollection(parent, child);
        }

        LinkInverse(child, navigation, parent);
    }

    // The navigation back from a loaded entity to the entity it was loaded from, as EF Core's no-tracking queries set it.
    private void LinkInverse(object target, BoundNavigation navigation, object source)
    {
        if (navigation.InverseName is not { } inverseName)
        {
            return;
        }

        var inverse = _bindEntity(navigation.TargetClrType).GetNavigation(inverseName);
        var accessor = EntityNavigations.Get(navigation.TargetClrType, inverseName);
        if (!inverse.IsCollection)
        {
            accessor.SetReference(target, source);
        }
        else if (_addedToCollections.Add((target, source)))
        {
            accessor.AddToCollection(target, source);
        }
    }

    private void MarkLoaded(object entity, BoundNavigation navigation)
    {
        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.Navigation(navigation.Name).IsLoaded = true;
        }
    }

    private static INavigationAccessor Accessor(object entity, BoundNavigation navigation)
        => EntityNavigations.Get(entity.GetType(), navigation.Name);

    private async ValueTask ReadAsync(SqlStatement statement, Action<DbDataReader> readRow, bool isAsync, CancellationToken cancellationToken)
    {
        if (!isAsync)
        {
            foreach (var _ in QueryExecutor.Stream(_context, _dialect, statement, row => { readRow(row); return true; }))
            {
            }

            return;
        }

        await foreach (var _ in QueryExecutor
            .StreamAsync(_context, _dialect, statement, row => { readRow(row); return true; }, cancellationToken)
            .ConfigureAwait(false))
        {
        }
    }

    /// <summary>How to read the entities of one statement's rows, and the parents each of its collections loads for.</summary>
    private sealed class StatementPlan
    {
        public StatementPlan(IncludeStatement statement)
        {
            Segments = [.. statement.Segments.Select(static s => new SegmentReader(s))];
            Loads = [.. statement.Collections.Select(load => new PendingLoad(
                load.Branch,
                load.Parent,
                new KeyReader(Segments[load.Parent].Entity, load.Branch.Navigation.SourceKey, Segments[load.Parent].Offset)))];
        }

        public IReadOnlyList<SegmentReader> Segments { get; }

        public IReadOnlyList<PendingLoad> Loads { get; }

        /// <summary>Records each entity of the row that a collection is loaded for, once per instance.</summary>
        public void CollectParents(DbDataReader row, object?[] entities)
        {
            foreach (var load in Loads)
            {
                if (entities[load.Segment] is { } parent && load.Seen.Add(parent))
                {
                    load.Parents.Add((parent, load.SourceKey.Read(row)));
                }
            }
        }
    }

    private sealed class PendingLoad(IncludeBranch branch, int segment, KeyReader sourceKey)
    {
        public IncludeBranch Branch { get; } = branch;

        public int Segment { get; } = segment;

        public KeyReader SourceKey { get; } = sourceKey;

        public List<(object Entity, object?[] Key)> Parents { get; } = [];

        public HashSet<object> Seen { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>Reads one entity from its columns in a row.</summary>
    private sealed class SegmentReader
    {
        private readonly int[] _ordinals;

        public SegmentReader(EntitySegment segment)
        {
            Entity = segment.Entity;
            Offset = segment.Offset;
            Parent = segment.Parent;
            Navigation = segment.Navigation;
            Operations = EntityReaders.GetOperations(Entity.ClrType);
            _ordinals = [.. Entity.GetOrdinals(Operations.PropertyNames).Select(o => o < 0 ? o : o + Offset)];
            Key = Entity.PrimaryKey is null ? null : new KeyReader(Entity, Offset);
            ShadowProperties = [.. Entity.ShadowProperties.Select(p => (p.Name, Offset + KeyReader.Ordinal(Entity, p), p.ClrType))];
        }

        public BoundEntity Entity { get; }

        public int Offset { get; }

        public int? Parent { get; }

        public BoundNavigation? Navigation { get; }

        public EntityOperations Operations { get; }

        public KeyReader? Key { get; }

        public IReadOnlyList<(string Name, int Ordinal, Type ClrType)> ShadowProperties { get; }

        /// <summary>Whether a left join found the entity: a missing one has null key columns.</summary>
        public bool IsPresent(DbDataReader row) => Key is null || !row.IsDBNull(Key.FirstOrdinal);

        public object Materialize(DbDataReader row) => Operations.Read(row, _ordinals);
    }

    private sealed class PairComparer : IEqualityComparer<(object Owner, object Item)>
    {
        public static PairComparer Instance { get; } = new();

        public bool Equals((object Owner, object Item) x, (object Owner, object Item) y)
            => ReferenceEquals(x.Owner, y.Owner) && ReferenceEquals(x.Item, y.Item);

        public int GetHashCode((object Owner, object Item) obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Owner), RuntimeHelpers.GetHashCode(obj.Item));
    }
}
