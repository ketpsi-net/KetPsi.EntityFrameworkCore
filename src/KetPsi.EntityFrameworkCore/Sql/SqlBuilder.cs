using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Filters;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>
/// Builds the SQL statement for an operator chain.
/// </summary>
/// <remarks>
/// <para>
/// Predicates are written so they never disagree with C#: negations are pushed down to the comparisons
/// (De Morgan), and each comparison is written so SQL's UNKNOWN only appears where C# evaluates to
/// <see langword="false"/>. With no <c>NOT</c> above a comparison, UNKNOWN behaves exactly like false.
/// </para>
/// <para>
/// LINQ applies operators in order, so an operator that follows <c>Skip</c> or <c>Take</c> works on the paged
/// rows. The builder then moves the statement so far into a derived table and keeps its ordering.
/// </para>
/// <para>
/// A projection is selected as late as EF Core selects it: paging after <c>Select</c> pages the entity rows, and the
/// navigations the projection reads are joined to the page. Only <c>Distinct</c>, which needs the projected columns,
/// selects it earlier.
/// </para>
/// </remarks>
internal sealed class SqlBuilder
{
    private const string AliasPrefix = "t";
    private const string AlwaysTrue = "1 = 1";
    private const string AlwaysFalse = "0 = 1";
    private const string ExistsProjection = "1";
    private const string RootPath = "";

    // The name of a computed column, such as an aggregate, in a derived table; repeats get a number.
    private const string GroupValueName = "c";

    // An entity query reads its entity, which is the projection of the root path.
    private static readonly Projection EntityProjection = new([], [RootPath]);

    private readonly BoundEntity _entity;
    private readonly ISqlDialect _dialect;
    private readonly IReadOnlyList<ResolvedQueryFilter> _filters;
    private readonly Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> _filtersFor;
    private readonly Func<Type, BoundEntity>? _bindEntity;
    private readonly IncludeTree? _includes;
    private readonly List<QueryParameter> _parameters = [];
    private readonly List<EntitySegment> _segments = [];
    private readonly List<CollectionLoad> _collections = [];
    private readonly List<int> _roots = [];

    // The entities joined by Join, by join path: "$0", "$1", ...
    private readonly Dictionary<string, BoundEntity> _joined = new(StringComparer.Ordinal);

    // The names of the DbParameters given to FromSql, which the statement's own parameters skip.
    private readonly HashSet<string> _givenParameterNames = new(StringComparer.OrdinalIgnoreCase);
    private int _aliasCount;
    private int _parameterCount;

    // The SQL of FromSql at the root, and whether an operator composes over it.
    private string? _rootSql;
    private bool _composed;
    private bool _readsFirstColumn;
    private SelectState? _state;

    private SqlBuilder(
        BoundEntity entity,
        ISqlDialect dialect,
        IReadOnlyList<ResolvedQueryFilter> filters,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor,
        Func<Type, BoundEntity>? bindEntity,
        IncludeTree? includes)
    {
        _entity = entity;
        _dialect = dialect;
        _filters = filters;
        _filtersFor = filtersFor ?? NoFilters;
        _bindEntity = bindEntity;
        _includes = includes;
    }

    private SelectState State => _state ?? throw new UnreachableException("The chain did not start with a root node.");

    /// <summary>Builds the statement for the chain ending at <paramref name="tail"/>.</summary>
    /// <param name="entity">The queried entity, bound to the model.</param>
    /// <param name="tail">The last node of the chain.</param>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="terminal">What the statement returns.</param>
    /// <param name="rowLimit">For <see cref="QueryTerminal.Rows"/>, a limit applied after every operator, such as 1 for <c>First</c>.</param>
    /// <param name="filters">The model query filters to apply to the root table, already resolved against the context.</param>
    /// <param name="aggregate">For <see cref="QueryTerminal.Aggregate"/>, what to compute.</param>
    /// <param name="filtersFor">The query filters to apply to an entity joined through a navigation.</param>
    /// <param name="bindEntity">Binds the target entity of a navigation that a predicate, ordering, projection or aggregate reads through.</param>
    public static SqlStatement Build(
        BoundEntity entity,
        QueryNode tail,
        ISqlDialect dialect,
        QueryTerminal terminal,
        int? rowLimit = null,
        IReadOnlyList<ResolvedQueryFilter>? filters = null,
        SqlAggregate? aggregate = null,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>>? filtersFor = null,
        Func<Type, BoundEntity>? bindEntity = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(dialect);
        if (rowLimit is { } limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit, nameof(rowLimit));
            if (terminal != QueryTerminal.Rows)
            {
                throw new ArgumentException(ExceptionMessages.RowLimitRequiresRows, nameof(rowLimit));
            }
        }

        var builder = new SqlBuilder(entity, dialect, filters ?? [], filtersFor, bindEntity, includes: null);
        builder.ApplyChain(tail);

        if (terminal == QueryTerminal.Aggregate && aggregate is null)
        {
            throw new ArgumentNullException(nameof(aggregate));
        }

        // SQL of FromSql that nothing composes over runs as it is, and its columns are read by name, as in EF Core; that of
        // SqlQuery of a scalar is read from its first column.
        if (terminal == QueryTerminal.Rows && rowLimit is null && builder.RunsAsWritten)
        {
            var byName = builder._readsFirstColumn ? null : entity.Properties.Select(static p => p.ColumnName).ToList();
            return new SqlStatement(builder._rootSql!, builder._parameters, byName);
        }

        var commandText = builder.Finish(terminal, rowLimit, aggregate);
        return new SqlStatement(commandText, builder._parameters);
    }

    /// <summary>
    /// Builds the statement that reads the entities of the chain ending at <paramref name="tail"/>, the queried entity
    /// or the ones a projection selects, together with their included references, and lists the included collections
    /// to load next.
    /// </summary>
    /// <param name="entity">The queried entity, bound to the model.</param>
    /// <param name="tail">The last node of the chain.</param>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="includes">
    /// The navigations included from the queried entity, or <see langword="null"/>. A projected entity gets the
    /// includes below its navigation path, as in EF Core; includes below no projected entity are ignored.
    /// </param>
    /// <param name="filtersFor">The query filters to apply to an entity, already resolved against the context.</param>
    /// <param name="rowLimit">A limit applied after every operator, such as 1 for <c>First</c>.</param>
    /// <param name="bindEntity">Binds the target entity of a navigation that a predicate, ordering or projection reads through.</param>
    public static IncludeStatement BuildRows(
        BoundEntity entity,
        QueryNode tail,
        ISqlDialect dialect,
        IncludeTree? includes,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> filtersFor,
        int? rowLimit = null,
        Func<Type, BoundEntity>? bindEntity = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(filtersFor);
        if (rowLimit is { } limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit, nameof(rowLimit));
        }

        var builder = new SqlBuilder(entity, dialect, filtersFor(entity), filtersFor, bindEntity, includes);
        builder.ApplyChain(tail);

        var commandText = builder.FinishRows(rowLimit);
        return builder.ToIncludeStatement(commandText);
    }

    /// <summary>
    /// Builds the statement that loads an included collection for the parents whose keys are in
    /// <paramref name="parentKeys"/>, with the references included from the collection's entity.
    /// </summary>
    /// <param name="branch">The included collection navigation.</param>
    /// <param name="parentKeys">
    /// The distinct values of the navigation's <see cref="BoundNavigation.SourceKey"/> on the loaded parents, each in
    /// key order. Must not be empty.
    /// </param>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="filtersFor">The query filters to apply to an entity, already resolved against the context.</param>
    public static IncludeStatement BuildCollection(
        IncludeBranch branch,
        IReadOnlyCollection<IReadOnlyList<object?>> parentKeys,
        ISqlDialect dialect,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> filtersFor)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(parentKeys);
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(filtersFor);
        if (!branch.Navigation.IsCollection)
        {
            throw new ArgumentException(ExceptionMessages.CollectionLoadRequiresCollection, nameof(branch));
        }

        if (parentKeys.Count == 0)
        {
            throw new ArgumentException(ExceptionMessages.CollectionLoadRequiresKeys, nameof(parentKeys));
        }

        var entity = branch.Target.Entity;
        var builder = new SqlBuilder(entity, dialect, filtersFor(entity), filtersFor, bindEntity: null, branch.Target);
        builder.Apply(new RootNode(entity.ClrType));
        builder.State.Predicates.Add(builder.RenderKeyMatch(branch.Navigation, parentKeys));

        // Children are grouped by parent in memory; within a parent they come in key order, as with EF Core.
        foreach (var key in entity.PrimaryKey ?? [])
        {
            builder.State.Ordering.Add(new Ordering(builder.RootColumn(key, builder.State.Alias), Descending: false));
        }

        var commandText = builder.FinishRows(rowLimit: null);
        return builder.ToIncludeStatement(commandText);
    }

    /// <summary>
    /// Builds the <c>ExecuteDelete</c> statement for the chain ending at <paramref name="tail"/>, as EF Core does: the
    /// rows the chain selects are deleted from the queried table. A chain without paging deletes from its own
    /// <c>FROM</c>, joins included; one with paging deletes the rows whose keys a subquery selects.
    /// </summary>
    /// <param name="entity">The queried entity, bound to the model.</param>
    /// <param name="tail">The last node of the chain: entity operators only, no projection or grouping.</param>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="includes">
    /// The navigations included from the queried entity, or <see langword="null"/>. Included references are joined, as in
    /// EF Core, so a required one whose target is filtered out keeps its rows.
    /// </param>
    /// <param name="filtersFor">The query filters to apply to an entity, already resolved against the context.</param>
    /// <param name="bindEntity">Binds the target entity of a navigation the chain reads through.</param>
    /// <exception cref="NotSupportedException">The chain pages a keyless entity, whose rows the subquery cannot name.</exception>
    public static SqlStatement BuildDelete(
        BoundEntity entity,
        QueryNode tail,
        ISqlDialect dialect,
        IncludeTree? includes,
        Func<BoundEntity, IReadOnlyList<ResolvedQueryFilter>> filtersFor,
        Func<Type, BoundEntity>? bindEntity = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(filtersFor);

        var builder = new SqlBuilder(entity, dialect, filtersFor(entity), filtersFor, bindEntity, includes);
        builder.ApplyChain(tail);

        return new SqlStatement(builder.FinishDelete(), builder._parameters);
    }

    private static IReadOnlyList<ResolvedQueryFilter> NoFilters(BoundEntity entity) => [];

    /// <summary>
    /// Applies the chain's nodes, after reserving the names of the DbParameters that FromSql was given anywhere in it,
    /// so the statement's own parameters never take them.
    /// </summary>
    private void ApplyChain(QueryNode tail)
    {
        var nodes = Chain(tail);
        foreach (var name in RawSources(nodes).SelectMany(static sql => sql.ParameterNames))
        {
            _givenParameterNames.Add(name.TrimStart('@'));
        }

        foreach (var node in nodes)
        {
            Apply(node);
        }

        // A query filter composes over the SQL too, as in EF Core.
        _composed |= _rootSql is not null && _filters.Count > 0;
    }

    private static IEnumerable<RawSql> RawSources(IEnumerable<QueryNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RootNode { Sql: { } sql }:
                    yield return sql;
                    break;
                case JoinNode join:
                    foreach (var inner in RawSources(Chain(join.Inner)))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    /// <summary>Whether the root is SQL of FromSql that no operator or query filter composes over.</summary>
    private bool RunsAsWritten => _rootSql is not null && !_composed;

    /// <summary>
    /// The SQL of FromSql with each placeholder replaced by its parameter: a new one for a value, the given one for a
    /// DbParameter. EF Core names the values <c>@p0</c>, <c>@p1</c>, ... in order, which the root's SQL gets too.
    /// </summary>
    private string RenderRawSql(RawSql sql)
    {
        if (sql.Arguments.Count == 0 && !sql.AlwaysFormat)
        {
            return sql.Format;
        }

        var substitutions = new object[sql.Arguments.Count];
        for (var i = 0; i < substitutions.Length; i++)
        {
            if (sql.Arguments[i] is System.Data.Common.DbParameter given)
            {
                _parameters.Add(new QueryParameter(given.ParameterName, given, Property: null));
                substitutions[i] = given.ParameterName;
            }
            else
            {
                substitutions[i] = AddParameter(sql.Arguments[i] ?? DBNull.Value, property: null);
            }
        }

        return string.Format(CultureInfo.InvariantCulture, sql.Format, substitutions);
    }

    /// <summary>The source that composes over SQL of FromSql: a derived table, after checking the SQL allows it.</summary>
    private static string Composed(string sql)
        => RawSql.IsComposable(sql)
            ? "(\n" + sql + "\n)"
            : throw new InvalidOperationException(ExceptionMessages.NonComposableSql);

    private static List<QueryNode> Chain(QueryNode tail)
    {
        var nodes = new List<QueryNode>();
        for (var node = tail; node is not null; node = node.Parent)
        {
            nodes.Add(node);
        }

        nodes.Reverse();
        return nodes;
    }

    private void Apply(QueryNode node)
    {
        _composed |= node is not (RootNode or TrackingNode or IgnoreQueryFiltersNode or ProjectionNode { ReadsTheSqlColumns: true });
        switch (node)
        {
            case RootNode root:
                ApplyRoot(root);
                break;
            case WhereNode where:
                EnsureEntityRows();
                WrapIf(State.HasPaging || State.Distinct);
                State.Predicates.Add(RenderEntityPredicate(where.Predicate, where.Values));
                break;
            case OrderNode { IsThenBy: false } order:
                EnsureEntityRows();
                WrapIf(State.HasPaging);
                State.Ordering.Clear();
                State.Ordering.Add(new Ordering(ResolvePath(order.PropertyName).Column, order.Descending));
                break;
            case OrderNode order:
                EnsureEntityRows();
                State.Ordering.Add(new Ordering(ResolvePath(order.PropertyName).Column, order.Descending));
                break;
            case JoinNode join:
                // Join works on the rows the operators selected, so paging moves into a derived table first, as in EF Core.
                EnsureEntityRows();
                WrapIf(State.HasPaging || State.Distinct);
                ApplyJoin(join);
                break;
            case SkipNode skip:
                WrapIf(State.HasPaging || State.Distinct);
                State.Offset = AddParameter(skip.Count, property: null);
                break;
            case TakeNode take:
                WrapIf(State.Limit is not null || State.Distinct);
                State.Limit = AddParameter(take.Count, property: null);
                break;
            case ProjectionNode projection when State.Keys is not null:
                // The groups themselves: EF Core reads each row's key and element ordered by the key, and groups them in
                // memory. Groups filtered or ordered by an aggregate can only be selected from.
                if (State.Having.Count > 0 || State.Ordering.Count > 0 || State.Columns is not null)
                {
                    throw new NotSupportedException(ExceptionMessages.GroupRowsAfterGroupOperators);
                }

                State.Ordering.AddRange(State.Keys.Select(static k => new Ordering(k.Column, Descending: false)));
                State.Keys = null;
                State.Pending = new Projection(projection.Columns, projection.EntityPaths);
                break;
            case ProjectionNode projection:
                // Distinct entity rows are made distinct before they are projected. A projection not selected yet is
                // replaced: the generator writes the paths of a Select after it through the entities it projects.
                EnsureEntityRows();
                WrapIf(State.Distinct);
                State.Pending = new Projection(projection.Columns, projection.EntityPaths);
                break;
            case DistinctNode:
                // Distinct applies to the rows the operators selected, and distinct rows have no order, as in EF Core.
                WrapIf(State.HasPaging);
                Materialize();
                State.Ordering.Clear();
                State.Distinct = true;
                break;
            case GroupByNode group:
                // Grouping works on the rows the operators selected; an ordering before it has no effect, as in EF Core.
                EnsureNotProjected();
                WrapIf(State.HasPaging || State.Distinct);
                State.Ordering.Clear();
                State.Keys = [.. group.KeyPaths.Select(ResolveKey)];
                break;
            case HavingNode having:
                EnsureGroupsNotPaged();
                State.Having.Add(RenderHaving(having));
                break;
            case GroupOrderNode { IsThenBy: false } order:
                EnsureGroupsNotPaged();
                State.Ordering.Clear();
                State.Ordering.Add(new Ordering(RenderGroupValue(order.Value, order.Values).Column, order.Descending));
                break;
            case GroupOrderNode order:
                EnsureGroupsNotPaged();
                State.Ordering.Add(new Ordering(RenderGroupValue(order.Value, order.Values).Column, order.Descending));
                break;
            case GroupSelectNode select:
                if (State.Keys is null || State.Columns is not null)
                {
                    throw new UnreachableException("A Select on groups follows GroupBy and the operators on its groups.");
                }

                State.Columns = [.. select.Values.Select(v => RenderGroupValue(v, select.PredicateValues).Column)];
                break;
            case ReverseOrderNode reverse:
                // Last reads the rows the operators selected from the end, so paging moves into a derived table first.
                if (State.Ordering.Count == 0)
                {
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.InvariantCulture, ExceptionMessages.LastWithoutOrdering, reverse.Method));
                }

                WrapIf(State.HasPaging);
                for (var i = 0; i < State.Ordering.Count; i++)
                {
                    State.Ordering[i] = State.Ordering[i] with { Descending = !State.Ordering[i].Descending };
                }

                break;
            case TrackingNode or IgnoreQueryFiltersNode or IncludeNode:
                // Tracking doesn't change the SQL, ignored filters were already left out of the resolved filters, and
                // includes are applied when the rows are read; scalar terminals ignore them, as in EF Core.
                break;
            default:
                throw new UnreachableException(node.GetType().ToString());
        }
    }

    /// <summary>
    /// For an operator on the entity rows: no projection selected yet, or one the generator reads through, with its
    /// paths written as the queried entity's and the joins'. Paging after such a projection ends that.
    /// </summary>
    private void EnsureEntityRows()
    {
        if (State.Columns is not null || State.Keys is not null || State.Pending is not null && (State.HasPaging || State.Distinct))
        {
            throw new UnreachableException("Where, OrderBy, Join and Select follow a projection only before any paging.");
        }
    }

    /// <summary>
    /// Joins the inner query of <paramref name="join"/> at the next join path: its table with its query filters in
    /// <c>ON</c>, and its predicates, which read the joined entity, added to the level's own.
    /// </summary>
    private void ApplyJoin(JoinNode join)
    {
        var innerNodes = Chain(join.Inner);
        var inner = Bind(((RootNode)innerNodes[0]).EntityType);
        var path = JoinNode.PathPrefix + _joined.Count.ToString(CultureInfo.InvariantCulture);

        // The outer keys can read through navigations, which join before this table.
        var outerKeys = join.OuterKeys.Select(key => ResolvePath(key).Column.Sql).ToList();
        var alias = NextAlias();
        var conditions = new List<string>();
        for (var i = 0; i < outerKeys.Count; i++)
        {
            conditions.Add(outerKeys[i] + " = " + Column(inner.GetProperty(join.InnerKeys[i]), alias));
        }

        foreach (var filter in _filtersFor(inner))
        {
            conditions.Add(RenderPredicate(inner.BindPredicate(filter.Predicate), filter.Values, negated: false, alias));
        }

        var innerSource = ((RootNode)innerNodes[0]).Sql is { } innerSql
            ? Composed(RenderRawSql(innerSql))
            : _dialect.QualifyTable(inner.Table, inner.Schema);
        State.Joins.Add("INNER JOIN " + innerSource + " AS " + alias + " ON " + string.Join(" AND ", conditions));
        State.Targets.Add(path, new JoinTarget(alias, inner, AlwaysExists: true));
        _joined.Add(path, inner);

        // An inner join keeps the rows that match in both, so the inner predicates can filter after the join.
        foreach (var node in innerNodes.Skip(1))
        {
            switch (node)
            {
                case WhereNode where:
                    State.Predicates.Add(RenderEntityPredicate(Prefixed(where.Predicate, path), where.Values));
                    break;
                case TrackingNode or IgnoreQueryFiltersNode:
                    break;
                default:
                    throw new UnreachableException("The inner query of a Join only filters its entity: " + node.GetType());
            }
        }
    }

    /// <summary>The predicate with each name read through <paramref name="path"/>, such as <c>"$0.Name"</c> for <c>"Name"</c>.</summary>
    private static PredicateTemplate Prefixed(PredicateTemplate template, string path) => template switch
    {
        ComparisonTemplate comparison => new ComparisonTemplate(path + "." + comparison.PropertyName, comparison.Comparison, comparison.ValueIndex),
        BooleanPropertyTemplate boolean => new BooleanPropertyTemplate(path + "." + boolean.PropertyName),
        NullCheckTemplate nullCheck => new NullCheckTemplate(path + "." + nullCheck.PropertyName, nullCheck.IsNull),
        InListTemplate inList => new InListTemplate(path + "." + inList.PropertyName, inList.ValueIndex),
        NotTemplate not => new NotTemplate(Prefixed(not.Operand, path)),
        AndTemplate and => new AndTemplate(Prefixed(and.Left, path), Prefixed(and.Right, path)),
        OrTemplate or => new OrTemplate(Prefixed(or.Left, path), Prefixed(or.Right, path)),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    private void EnsureRootComposable()
    {
        if (_rootSql is not null)
        {
            _ = Composed(_rootSql);
        }
    }

    // The generator only lets paging, Distinct and terminals follow Select, and only operators on groups follow GroupBy.
    private void EnsureNotProjected()
    {
        if (State.Pending is not null || State.Columns is not null || State.Keys is not null)
        {
            throw new UnreachableException("Only paging, Distinct and terminals can follow a projection.");
        }
    }

    // The generator lets Where and OrderBy on groups follow GroupBy and its Select, but not paging.
    private void EnsureGroupsNotPaged()
    {
        if (State.Keys is null || State.HasPaging)
        {
            throw new UnreachableException("Where and OrderBy on groups follow GroupBy, before any paging.");
        }
    }

    /// <summary>The column a key path reads, joining the navigations it goes through.</summary>
    private GroupKey ResolveKey(string path)
    {
        var dot = path.LastIndexOf('.');
        var target = GetTarget(dot < 0 ? RootPath : path[..dot], entity: null);
        var property = target.Entity.GetProperty(path[(dot + 1)..]);

        // A key read through a missing optional reference is null.
        return new GroupKey(RootColumn(property, target.Alias), property with { IsNullable = property.IsNullable || !target.AlwaysExists });
    }

    /// <summary>
    /// The SQL of a key part or aggregate at the grouped level, and a property that describes its value: its store type
    /// types the parameters it is compared with, and its nullability decides how comparisons treat null.
    /// </summary>
    private (SqlColumn Column, BoundProperty Value) RenderGroupValue(GroupValue value, IReadOnlyList<object?> values)
    {
        var keys = State.Keys ?? throw new UnreachableException("Group values are read after GroupBy.");
        switch (value.Kind)
        {
            case GroupValueKind.Key:
                if (value.KeyIndex >= keys.Count)
                {
                    throw new ArgumentException(string.Format(
                        CultureInfo.InvariantCulture, ExceptionMessages.GroupKeyIndexOutOfRange, value.KeyIndex, keys.Count));
                }

                var key = keys[value.KeyIndex];
                return (key.Column, key.Value);

            case GroupValueKind.Count or GroupValueKind.LongCount:
                var isLong = value.Kind == GroupValueKind.LongCount;
                var sql = value.Predicate is { } predicate
                    ? _dialect.ConditionalCount(RenderPredicate(_entity.BindPredicate(predicate), values, negated: false, State.Alias), isLong)
                    : _dialect.Count(isLong);
                return (Computed(sql), CountValue(isLong));

            case GroupValueKind.DistinctCount:
                var distinctLong = value.ValueType == AggregateValueType.Int64;
                var distinct = _dialect.DistinctCount(Column(_entity.GetProperty(value.PropertyName!), State.Alias), distinctLong);
                return (Computed(distinct), CountValue(distinctLong));

            case GroupValueKind.Aggregate:
                var property = _entity.GetProperty(value.PropertyName!);
                var aggregate = _dialect.Aggregate(value.Function, Column(property, State.Alias), value.ValueType);
                return (Computed(aggregate), AggregateValue(value, property));

            default:
                throw new UnreachableException(value.Kind.ToString());
        }
    }

    private static SqlColumn Computed(string sql) => new(sql, GroupValueName, IsComputed: true);

    private static BoundProperty CountValue(bool isLong)
        => isLong
            ? new BoundProperty(GroupValueName, GroupValueName, "bigint", IsNullable: false, typeof(long), IsShadow: false)
            : new BoundProperty(GroupValueName, GroupValueName, "int", IsNullable: false, typeof(int), IsShadow: false);

    // Sum is coalesced to zero; Min, Max and Average of a group are null only when every value is.
    private static BoundProperty AggregateValue(GroupValue value, BoundProperty property)
    {
        (string StoreType, Type ClrType) result = (value.Function, value.ValueType) switch
        {
            (AggregateFunction.Min or AggregateFunction.Max, _) => (property.StoreType, property.ClrType),
            (AggregateFunction.Average, AggregateValueType.Int32 or AggregateValueType.Int64 or AggregateValueType.Double) => ("float", typeof(double)),
            (_, AggregateValueType.Int32) => ("int", typeof(int)),
            (_, AggregateValueType.Int64) => ("bigint", typeof(long)),
            (_, AggregateValueType.Double) => ("float", typeof(double)),
            (_, AggregateValueType.Single) => ("real", typeof(float)),
            (_, AggregateValueType.Decimal) => ("decimal", typeof(decimal)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.ValueType, ExceptionMessages.GroupAggregateNeedsNumericType),
        };

        var nullable = value.Function != AggregateFunction.Sum && property.IsNullable;
        return new BoundProperty(GroupValueName, GroupValueName, result.StoreType, nullable, result.ClrType, IsShadow: false);
    }

    /// <summary>A <c>HAVING</c> condition: the predicate with each operand name replaced by that operand's SQL.</summary>
    private string RenderHaving(HavingNode having)
    {
        var operands = having.Operands.Select(o => RenderGroupValue(o, having.Values)).ToArray();
        var sql = new Dictionary<BoundProperty, string>(ReferenceEqualityComparer.Instance);
        BoundProperty Resolve(string name)
        {
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= operands.Length)
            {
                throw new ArgumentException(string.Format(
                    CultureInfo.InvariantCulture, ExceptionMessages.HavingOperandOutOfRange, name, operands.Length));
            }

            // Each use gets its own instance, so the same value read twice maps to its SQL either way.
            var operand = operands[index].Value with { Name = name };
            sql[operand] = operands[index].Column.Sql;
            return operand;
        }

        var predicate = PredicateBinder.Bind(having.Predicate, Resolve, typeof(IGrouping<,>));
        return RenderPredicate(predicate, having.Values, negated: false, property => sql[property]);
    }

    private void ApplyRoot(RootNode root)
    {
        if (_state is not null)
        {
            throw new UnreachableException("A chain has exactly one root node.");
        }

        if (root.EntityType != _entity.ClrType)
        {
            throw new ArgumentException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.RootEntityMismatch, root.EntityType, _entity.ClrType));
        }

        if (root.Sql is { } sql)
        {
            // Rendered first, so its values are @p0, @p1, ... as in EF Core; checked when a statement composes over it.
            _rootSql = RenderRawSql(sql);
            _readsFirstColumn = root.ReadsFirstColumn;
            _state = new SelectState("(\n" + _rootSql + "\n)", NextAlias(), isDerived: true);
        }
        else
        {
            _state = new SelectState(_dialect.QualifyTable(_entity.Table, _entity.Schema), NextAlias());
        }

        // Query filters restrict the entity set itself, so they apply before any operator, like in EF Core.
        foreach (var filter in _filters)
        {
            var predicate = _entity.BindPredicate(filter.Predicate);
            _state.Predicates.Add(RenderPredicate(predicate, filter.Values, negated: false, _state.Alias));
        }
    }

    private string Finish(QueryTerminal terminal, int? rowLimit, SqlAggregate? aggregate)
    {
        EnsureRootComposable();
        switch (terminal)
        {
            case QueryTerminal.Rows:
                if (State.Keys is not null && State.Columns is null)
                {
                    throw new NotSupportedException(ExceptionMessages.GroupEnumerationNotSupported);
                }

                Materialize();
                ApplyRowLimit(rowLimit);
                return RenderSelect(State, SelectList(State), includeOrdering: true);

            case QueryTerminal.Count or QueryTerminal.LongCount:
                // A projection that isn't distinct has one row per entity row, so it isn't selected: its navigations
                // are not joined, as in EF Core. Groups are counted as the rows of a derived table.
                WrapIf(State.HasPaging || State.Distinct || State.Keys is not null);
                return RenderSelect(State, _dialect.Count(terminal == QueryTerminal.LongCount), includeOrdering: false);

            case QueryTerminal.Aggregate:
                // Like Count, an aggregate works on the rows the operators selected, and over groups on a derived table.
                WrapIf(State.HasPaging || State.Distinct || State.Keys is not null);
                var column = AggregatedColumn(aggregate!);
                return RenderSelect(State, _dialect.Aggregate(aggregate!.Function, column, aggregate.ValueType), includeOrdering: false);

            case QueryTerminal.Any:
                // ORDER BY is only valid in a subquery that pages, and only matters there.
                return _dialect.Exists(RenderSelect(State, ExistsProjection, includeOrdering: State.HasPaging));

            default:
                throw new UnreachableException(terminal.ToString());
        }
    }

    private string FinishRows(int? rowLimit)
    {
        EnsureRootComposable();
        if (State.Pending is null && State.Columns is null)
        {
            State.Pending = EntityProjection;
        }

        // EF Core orders by the key last when it loads collections, so a query's rows come in the same order. Every row
        // has one queried entity, so its key orders the rows of a projected entity too.
        if (State.Pending is { } pending && pending.EntityPaths.Any(path => _includes?.Find(path) is { HasCollections: true }))
        {
            foreach (var key in _entity.PrimaryKey ?? [])
            {
                var column = RootColumn(key, State.Alias);
                if (!State.Ordering.Exists(o => o.Column.Sql == column.Sql))
                {
                    State.Ordering.Add(new Ordering(column, Descending: false));
                }
            }
        }

        Materialize();
        ApplyRowLimit(rowLimit);
        return RenderSelect(State, SelectList(State), includeOrdering: true);
    }

    private string FinishDelete()
    {
        EnsureRootComposable();
        EnsureNotProjected();
        JoinIncludedReferences(_includes, RootPath);

        // Without paging the rows are the ones the statement's FROM selects; distinct entity rows are the same rows.
        if (!State.IsDerived && !State.HasPaging)
        {
            return _dialect.Delete(State.Alias, RenderFrom(State));
        }

        var key = _entity.PrimaryKey ?? throw new NotSupportedException(string.Format(
            CultureInfo.InvariantCulture, ExceptionMessages.DeletePagedKeyless, _entity.ClrType));
        var selected = RenderSelect(State, string.Join(", ", key.Select(k => Column(k, State.Alias))), includeOrdering: State.HasPaging);

        var alias = NextAlias();
        var table = _dialect.QualifyTable(_entity.Table, _entity.Schema) + " AS " + alias;
        if (key is [var single])
        {
            return _dialect.Delete(alias, table + " WHERE " + Column(single, alias) + " IN (" + selected + ")");
        }

        var keys = NextAlias();
        var matches = string.Join(" AND ", key.Select(k => keys + "." + _dialect.QuoteIdentifier(k.ColumnName) + " = " + Column(k, alias)));
        return _dialect.Delete(alias, table + " WHERE EXISTS (SELECT 1 FROM (" + selected + ") AS " + keys + " WHERE " + matches + ")");
    }

    // EF Core joins the references included on the deleted entity, which a required one with a filtered target can narrow.
    private void JoinIncludedReferences(IncludeTree? includes, string path)
    {
        foreach (var branch in includes?.Branches ?? [])
        {
            if (!branch.Navigation.IsCollection)
            {
                var branchPath = Append(path, branch.Navigation.Name);
                GetTarget(branchPath, branch.Target.Entity);
                JoinIncludedReferences(branch.Target, branchPath);
            }
        }
    }

    private IncludeStatement ToIncludeStatement(string commandText)
        => new(new SqlStatement(commandText, _parameters), _segments, _collections, _roots);

    private void ApplyRowLimit(int? rowLimit)
    {
        if (rowLimit is { } limit)
        {
            WrapIf(State.Limit is not null || State.Distinct);
            State.Limit = limit.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Selects the pending projection at the current level: its columns, then its entities with the references
    /// included on them, joining every navigation they are read through.
    /// </summary>
    private void Materialize(bool wrapForNavigations = true)
    {
        if (State.Pending is not { } projection)
        {
            return;
        }

        // Navigations join the rows the operators selected, so paging moves into a derived table first.
        WrapIf(wrapForNavigations && State.HasPaging && Joins(projection));

        var columns = new List<SqlColumn>();
        foreach (var path in projection.Columns)
        {
            columns.Add(ResolveColumn(path));
        }

        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in projection.EntityPaths)
        {
            _roots.Add(AddEntity(path, _includes?.Find(path), parentSegment: null, navigation: null, columns, offsets));
        }

        State.Pending = null;
        State.Columns = columns;
    }

    // Whether selecting the projection joins navigations. The joined entities of Join are already joined at the level.
    private bool Joins(Projection projection)
        => projection.Columns.Any(static path => path.Contains('.', StringComparison.Ordinal) && !IsThroughJoin(path))
            || projection.EntityPaths.Any(path => path.Length > 0 && !IsThroughJoin(path)
                || (_includes?.Find(path)?.Branches.Any(static b => !b.Navigation.IsCollection) ?? false));

    private static bool IsThroughJoin(string path) => path.Length > 1 && path[0] == JoinNode.PathPrefix;

    private static bool ReadsJoins(Projection projection)
        => projection.Columns.Any(IsThroughJoin) || projection.EntityPaths.Any(IsThroughJoin);

    /// <summary>
    /// Adds an entity's columns, once per path, and a segment that reads them, then the references included from it
    /// and the collections to load for it. A repeated path gets segments of its own over the same columns.
    /// </summary>
    private int AddEntity(
        string path,
        IncludeTree? includes,
        int? parentSegment,
        BoundNavigation? navigation,
        List<SqlColumn> columns,
        Dictionary<string, int> offsets)
    {
        var target = GetTarget(path, includes?.Entity);
        if (!offsets.TryGetValue(path, out var offset))
        {
            offset = columns.Count;
            offsets.Add(path, offset);
            columns.AddRange(target.Entity.Properties.Select(p => RootColumn(p, target.Alias)));
        }

        var segment = _segments.Count;
        _segments.Add(new EntitySegment(target.Entity, offset, parentSegment, navigation));
        foreach (var branch in includes?.Branches ?? [])
        {
            if (branch.Navigation.IsCollection)
            {
                _collections.Add(new CollectionLoad(segment, branch));
                continue;
            }

            AddEntity(Append(path, branch.Navigation.Name), branch.Target, segment, branch.Navigation, columns, offsets);
        }

        return segment;
    }

    /// <summary>
    /// A predicate on the queried entity at the current level. Its names are property paths, which join the reference
    /// navigations they go through, and a null check can name a navigation itself.
    /// </summary>
    private string RenderEntityPredicate(PredicateTemplate template, IReadOnlyList<object?> values)
    {
        if (TemplateNames(template).All(_entity.HasProperty))
        {
            return RenderPredicate(_entity.BindPredicate(template), values, negated: false, State.Alias);
        }

        // Each use gets its own instance, so the same path read twice maps to its column either way.
        var columns = new Dictionary<BoundProperty, string>(ReferenceEqualityComparer.Instance);
        BoundProperty Resolve(string path)
        {
            var (column, property) = ResolvePath(path);
            var operand = property with { };
            columns[operand] = column.Sql;
            return operand;
        }

        var predicate = PredicateBinder.Bind(ExpandNavigationChecks(template), Resolve, _entity.ClrType);
        return RenderPredicate(predicate, values, negated: false, property => columns[property]);
    }

    private static IEnumerable<string> TemplateNames(PredicateTemplate template) => template switch
    {
        ComparisonTemplate comparison => [comparison.PropertyName],
        BooleanPropertyTemplate boolean => [boolean.PropertyName],
        NullCheckTemplate nullCheck => [nullCheck.PropertyName],
        InListTemplate inList => [inList.PropertyName],
        NotTemplate not => TemplateNames(not.Operand),
        AndTemplate and => TemplateNames(and.Left).Concat(TemplateNames(and.Right)),
        OrTemplate or => TemplateNames(or.Left).Concat(TemplateNames(or.Right)),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    /// <summary>
    /// Replaces each null check of a reference navigation (<c>x.Blog == null</c>) with null checks of the target's key
    /// that joins it, as EF Core does. <see cref="ResolvePath"/> reads that key from the foreign key when it can.
    /// </summary>
    private PredicateTemplate ExpandNavigationChecks(PredicateTemplate template)
    {
        switch (template)
        {
            case NullCheckTemplate nullCheck when NavigationAt(nullCheck.PropertyName) is var (_, navigation):
                return navigation.TargetKey
                    .Select(key => nullCheck.PropertyName + "." + key)
                    .Select(PredicateTemplate (key) => new NullCheckTemplate(key, nullCheck.IsNull))
                    .Aggregate((left, right) => nullCheck.IsNull ? new OrTemplate(left, right) : new AndTemplate(left, right));
            case NotTemplate not:
                var operand = ExpandNavigationChecks(not.Operand);
                return ReferenceEquals(operand, not.Operand) ? not : new NotTemplate(operand);
            case AndTemplate and:
                var (andLeft, andRight) = (ExpandNavigationChecks(and.Left), ExpandNavigationChecks(and.Right));
                return ReferenceEquals(andLeft, and.Left) && ReferenceEquals(andRight, and.Right) ? and : new AndTemplate(andLeft, andRight);
            case OrTemplate or:
                var (orLeft, orRight) = (ExpandNavigationChecks(or.Left), ExpandNavigationChecks(or.Right));
                return ReferenceEquals(orLeft, or.Left) && ReferenceEquals(orRight, or.Right) ? or : new OrTemplate(orLeft, orRight);
            default:
                return template;
        }
    }

    /// <summary>The navigation a path ends with and the path of the entity that has it, or null when it ends with a property.</summary>
    private (string ParentPath, BoundNavigation Navigation)? NavigationAt(string path)
    {
        if (JoinNode.IsJoinPath(path))
        {
            return null;
        }

        var dot = path.LastIndexOf('.');
        var parentPath = dot < 0 ? RootPath : path[..dot];
        var parent = EntityAt(parentPath);
        var name = path[(dot + 1)..];
        return parent.HasProperty(name) ? null : (parentPath, parent.GetNavigation(name));
    }

    /// <summary>The entity a navigation path reaches, without joining it; the path can start at a joined entity.</summary>
    private BoundEntity EntityAt(string path)
    {
        var entity = _entity;
        var names = path.Length == 0 ? [] : path.Split('.');
        for (var i = 0; i < names.Length; i++)
        {
            entity = i == 0 && _joined.TryGetValue(names[0], out var joined) ? joined : Bind(entity.GetNavigation(names[i]).TargetClrType);
        }

        return entity;
    }

    /// <summary>
    /// The column a property path of a predicate or an ordering reads, and its property, nullable when a navigation on
    /// the way can be missing. A key of a navigation's target is read from the foreign key that joins it, without
    /// joining the target, as in EF Core; unless the target has query filters, which only the join applies.
    /// </summary>
    private (SqlColumn Column, BoundProperty Property) ResolvePath(string path)
    {
        var dot = path.LastIndexOf('.');
        if (dot < 0)
        {
            var property = _entity.GetProperty(path);
            return (RootColumn(property, State.Alias), property);
        }

        var navigationPath = path[..dot];
        var name = path[(dot + 1)..];
        if (NavigationAt(navigationPath) is var (parentPath, navigation)
            && navigation is { IsOnDependent: true, IsCollection: false }
            && IndexOf(navigation.TargetKey, name) is >= 0 and var index
            && _filtersFor(Bind(navigation.TargetClrType)).Count == 0)
        {
            var parent = GetTarget(parentPath, entity: null);
            var key = navigation.SourceKey[index];
            return (RootColumn(key, parent.Alias), key with { IsNullable = key.IsNullable || !parent.AlwaysExists });
        }

        var target = GetTarget(navigationPath, entity: null);
        var read = target.Entity.GetProperty(name);
        return (RootColumn(read, target.Alias), read with { IsNullable = read.IsNullable || !target.AlwaysExists });
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The column a property path reads, joining the navigations it goes through.</summary>
    private SqlColumn ResolveColumn(string path)
    {
        var dot = path.LastIndexOf('.');
        var target = GetTarget(dot < 0 ? RootPath : path[..dot], entity: null);
        return RootColumn(target.Entity.GetProperty(path[(dot + 1)..]), target.Alias);
    }

    /// <summary>
    /// The table a navigation path reaches, joined at the current level on first use: <see cref="RootPath"/> is the
    /// queried entity, and each reference navigation is joined to the table before it.
    /// </summary>
    /// <param name="path">The navigation path.</param>
    /// <param name="entity">The bound target entity, when the caller has it.</param>
    /// <exception cref="NotSupportedException">The path goes through a collection navigation.</exception>
    private JoinTarget GetTarget(string path, BoundEntity? entity)
    {
        if (State.Targets.TryGetValue(path, out var existing))
        {
            return existing;
        }

        if (path.Length == 0)
        {
            var root = new JoinTarget(State.Alias, _entity, AlwaysExists: true);
            State.Targets.Add(path, root);
            return root;
        }

        var dot = path.LastIndexOf('.');
        var parent = GetTarget(dot < 0 ? RootPath : path[..dot], entity: null);
        var name = path[(dot + 1)..];
        var navigation = parent.Entity.GetNavigation(name);
        if (navigation.IsCollection)
        {
            throw new NotSupportedException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.CollectionNavigationInProjection, parent.Entity.ClrType, name));
        }

        var targetEntity = entity ?? Bind(navigation.TargetClrType);
        var alias = NextAlias();

        // A required reference always has its target, unless a reference before it was missing.
        var inner = navigation.IsRequired && parent.AlwaysExists;
        var conditions = new List<string>();
        for (var i = 0; i < navigation.SourceKey.Count; i++)
        {
            conditions.Add(
                Column(navigation.SourceKey[i], parent.Alias) + " = " + Column(targetEntity.GetProperty(navigation.TargetKey[i]), alias));
        }

        // Filtering the joined rows in ON is the same as joining a filtered table, as EF Core does.
        foreach (var filter in _filtersFor(targetEntity))
        {
            conditions.Add(RenderPredicate(targetEntity.BindPredicate(filter.Predicate), filter.Values, negated: false, alias));
        }

        State.Joins.Add(
            (inner ? "INNER JOIN " : "LEFT JOIN ") + _dialect.QualifyTable(targetEntity.Table, targetEntity.Schema) + " AS " + alias +
            " ON " + string.Join(" AND ", conditions));

        var target = new JoinTarget(alias, targetEntity, inner);
        State.Targets.Add(path, target);
        return target;
    }

    private BoundEntity Bind(Type entityType)
        => _bindEntity?.Invoke(entityType) ?? throw new UnreachableException("A query that reads through a navigation needs an entity binder.");

    private static string Append(string path, string name) => path.Length == 0 ? name : path + "." + name;

    private string RenderKeyMatch(BoundNavigation navigation, IReadOnlyCollection<IReadOnlyList<object?>> parentKeys)
    {
        var columns = navigation.TargetKey.Select(_entity.GetProperty).ToArray();
        if (columns.Length == 1)
        {
            var list = AddParameter(_dialect.CreateListParameterValue(parentKeys.Select(static k => k[0])), property: null);
            return _dialect.InList(Column(columns[0], State.Alias), list, columns[0].StoreType);
        }

        var tuples = AddParameter(_dialect.CreateTupleListParameterValue(parentKeys), property: null);
        return _dialect.InTupleList([.. columns.Select(c => Column(c, State.Alias))], tuples, [.. columns.Select(static c => c.StoreType)]);
    }

    private void WrapIf(bool condition)
    {
        if (!condition)
        {
            return;
        }

        // The joined entities of Join stay at this level, so a projection that reads them is selected here.
        if (State.Pending is { } pending && ReadsJoins(pending))
        {
            Materialize(wrapForNavigations: false);
        }

        var inner = State;

        // The derived table selects what the outer query reads: the projected columns, the entity columns a pending
        // projection reads, or every entity column; then the ordering columns, which the outer query orders by.
        List<SqlColumn> selected = inner.Columns is { } columns
            ? [.. columns]
            : inner.Keys is { } keys
                ? [.. keys.Select(static k => k.Column)]
                : [.. RootProperties(inner.Pending).Select(p => RootColumn(p, inner.Alias))];
        foreach (var ordering in inner.Ordering)
        {
            if (!selected.Exists(c => c.Sql == ordering.Column.Sql))
            {
                selected.Add(ordering.Column);
            }
        }

        var names = UniqueNames(selected);
        var selectList = string.Join(
            ", ", selected.Select((c, i) => names[i] == c.Name && !c.IsComputed ? c.Sql : c.Sql + " AS " + _dialect.QuoteIdentifier(names[i])));
        var source = "(" + RenderSelect(inner, selectList, includeOrdering: true) + ")";

        var outer = new SelectState(source, NextAlias(), isDerived: true) { Pending = inner.Pending };
        SqlColumn Outer(int index) => new(outer.Alias + "." + _dialect.QuoteIdentifier(names[index]), names[index]);
        if (inner.Columns is not null || inner.Keys is not null)
        {
            outer.Columns = [.. Enumerable.Range(0, inner.Columns?.Count ?? inner.Keys!.Count).Select(Outer)];
        }

        outer.Ordering.AddRange(inner.Ordering.Select(o => o with { Column = Outer(selected.FindIndex(c => c.Sql == o.Column.Sql)) }));
        _state = outer;
    }

    // The entity columns a derived table selects: every one, or only those a pending projection reads.
    private IEnumerable<BoundProperty> RootProperties(Projection? pending)
    {
        if (pending is null || pending.EntityPaths.Contains(RootPath))
        {
            return _entity.Properties;
        }

        var properties = new List<BoundProperty>();
        foreach (var path in pending.Columns)
        {
            var dot = path.IndexOf('.', StringComparison.Ordinal);
            properties.AddRange(dot < 0 ? [_entity.GetProperty(path)] : _entity.GetNavigation(path[..dot]).SourceKey);
        }

        foreach (var path in pending.EntityPaths)
        {
            var dot = path.IndexOf('.', StringComparison.Ordinal);
            properties.AddRange(_entity.GetNavigation(dot < 0 ? path : path[..dot]).SourceKey);
        }

        return properties.Distinct();
    }

    // Column names in a derived table must be unique, so a repeated one gets a number, as in EF Core.
    private static string[] UniqueNames(List<SqlColumn> columns)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string[columns.Count];
        for (var i = 0; i < names.Length; i++)
        {
            var name = columns[i].Name;
            for (var n = 0; !used.Add(name); n++)
            {
                name = columns[i].Name + n.ToString(CultureInfo.InvariantCulture);
            }

            names[i] = name;
        }

        return names;
    }

    /// <summary>The level's source with its alias, its joins and its <c>WHERE</c> clause.</summary>
    private static string RenderFrom(SelectState state)
    {
        var sql = new StringBuilder(state.Source).Append(" AS ").Append(state.Alias);
        foreach (var join in state.Joins)
        {
            sql.Append(' ').Append(join);
        }

        if (state.Predicates.Count > 0)
        {
            sql.Append(" WHERE ").AppendJoin(" AND ", state.Predicates);
        }

        return sql.ToString();
    }

    private string RenderSelect(SelectState state, string projection, bool includeOrdering)
    {
        var hasOrderBy = includeOrdering && state.Ordering.Count > 0;
        var paging = _dialect.Paging(state.Offset, state.Limit, hasOrderBy);

        var sql = new StringBuilder("SELECT ");
        if (state.Distinct)
        {
            sql.Append("DISTINCT ");
        }

        if (paging.SelectPrefix is not null)
        {
            sql.Append(paging.SelectPrefix).Append(' ');
        }

        sql.Append(projection).Append(" FROM ").Append(RenderFrom(state));

        if (state.Keys is { } keys)
        {
            sql.Append(" GROUP BY ").AppendJoin(", ", keys.Select(static k => k.Column.Sql));
        }

        if (state.Having.Count > 0)
        {
            sql.Append(" HAVING ").AppendJoin(" AND ", state.Having);
        }

        if (hasOrderBy)
        {
            sql.Append(" ORDER BY ").AppendJoin(", ", state.Ordering.Select(static o => o.Descending ? o.Column.Sql + " DESC" : o.Column.Sql));
        }
        else if (paging.FallbackOrderBy is not null)
        {
            sql.Append(" ORDER BY ").Append(paging.FallbackOrderBy);
        }

        if (paging.Suffix is not null)
        {
            sql.Append(' ').Append(paging.Suffix);
        }

        return sql.ToString();
    }

    private string RenderPredicate(BoundPredicate predicate, IReadOnlyList<object?> values, bool negated, string alias)
        => RenderPredicate(predicate, values, negated, ColumnsAt(alias));

    private string RenderPredicate(BoundPredicate predicate, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column) => predicate switch
    {
        BoundAnd and => Combine(
            negated ? "OR" : "AND", RenderPredicate(and.Left, values, negated, column), RenderPredicate(and.Right, values, negated, column)),
        BoundOr or => Combine(
            negated ? "AND" : "OR", RenderPredicate(or.Left, values, negated, column), RenderPredicate(or.Right, values, negated, column)),
        BoundNot not => RenderPredicate(not.Operand, values, !negated, column),
        BoundComparison comparison => RenderComparison(comparison, values[comparison.ValueIndex], negated, column),
        BoundBooleanProperty boolean =>
            column(boolean.Property) + " = " + (negated ? _dialect.FalseLiteral : _dialect.TrueLiteral),
        BoundNullCheck nullCheck => column(nullCheck.Property) + (nullCheck.IsNull != negated ? " IS NULL" : " IS NOT NULL"),
        BoundInList inList => RenderInList(inList, values[inList.ValueIndex], negated, column),
        _ => throw new UnreachableException(predicate.GetType().ToString()),
    };

    private string RenderComparison(BoundComparison comparison, object? value, bool negated, Func<BoundProperty, string> columnOf)
    {
        var column = columnOf(comparison.Property);
        var effective = negated ? Negate(comparison.Comparison) : comparison.Comparison;

        if (value is null)
        {
            // C# lifts comparisons with null: == and != test for null, every ordering comparison is false.
            return effective switch
            {
                ComparisonOperator.Equal => column + " IS NULL",
                ComparisonOperator.NotEqual => column + " IS NOT NULL",
                _ => negated ? AlwaysTrue : AlwaysFalse,
            };
        }

        var sql = column + " " + OperatorSql(effective) + " " + AddParameter(value, comparison.Property);

        // In C#, comparing a null property with a non-null value is false for every operator except !=.
        var trueWhenPropertyIsNull = (comparison.Comparison == ComparisonOperator.NotEqual) != negated;
        return comparison.Property.IsNullable && trueWhenPropertyIsNull
            ? Combine("OR", sql, column + " IS NULL")
            : sql;
    }

    private string RenderInList(BoundInList inList, object? value, bool negated, Func<BoundProperty, string> columnOf)
    {
        if (value is not IEnumerable list)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.ContainsListNotEnumerable, inList.Property.Name, value?.GetType()));
        }

        // Nulls never match inside IN, and would make NOT IN unknown for every row, so they are handled with IS NULL.
        var nonNullValues = new List<object?>();
        var hasNull = false;
        foreach (var item in list)
        {
            if (item is null)
            {
                hasNull = true;
            }
            else
            {
                nonNullValues.Add(item);
            }
        }

        var column = columnOf(inList.Property);
        var parameter = AddParameter(_dialect.CreateListParameterValue(nonNullValues), property: null);
        var inSql = _dialect.InList(column, parameter, inList.Property.StoreType);
        var sql = negated ? "NOT (" + inSql + ")" : inSql;

        var trueWhenPropertyIsNull = hasNull != negated;
        return inList.Property.IsNullable && trueWhenPropertyIsNull
            ? Combine("OR", sql, column + " IS NULL")
            : sql;
    }

    private static string Combine(string logicalOperator, string left, string right)
        => "(" + left + " " + logicalOperator + " " + right + ")";

    private static ComparisonOperator Negate(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => ComparisonOperator.NotEqual,
        ComparisonOperator.NotEqual => ComparisonOperator.Equal,
        ComparisonOperator.LessThan => ComparisonOperator.GreaterThanOrEqual,
        ComparisonOperator.LessThanOrEqual => ComparisonOperator.GreaterThan,
        ComparisonOperator.GreaterThan => ComparisonOperator.LessThanOrEqual,
        ComparisonOperator.GreaterThanOrEqual => ComparisonOperator.LessThan,
        _ => throw new UnreachableException(comparison.ToString()),
    };

    private static string OperatorSql(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "<>",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        _ => throw new UnreachableException(comparison.ToString()),
    };

    // The column the selector's property path reads, or the one column a projection selects.
    private string AggregatedColumn(SqlAggregate aggregate)
    {
        if (aggregate.PropertyName is { } path)
        {
            EnsureNotProjected();
            return ResolveColumn(path).Sql;
        }

        Materialize();
        if (_roots.Count > 0)
        {
            throw new InvalidOperationException(ExceptionMessages.AggregateNeedsColumnOnly);
        }

        return State.Columns is [var single]
            ? single.Sql
            : throw new InvalidOperationException(ExceptionMessages.AggregateNeedsOneColumn);
    }

    // The projected columns, or every entity column for an entity query.
    private string SelectList(SelectState state)
        => state.Columns is null ? EntityColumns(state.Alias) : string.Join(", ", state.Columns.Select(static c => c.Sql));

    private string EntityColumns(string alias) => string.Join(", ", _entity.Properties.Select(p => Column(p, alias)));

    private SqlColumn RootColumn(BoundProperty property, string alias) => new(Column(property, alias), property.ColumnName);

    private string Column(BoundProperty property, string alias) => alias + "." + _dialect.QuoteIdentifier(property.ColumnName);

    private Func<BoundProperty, string> ColumnsAt(string alias) => property => Column(property, alias);

    private string AddParameter(object value, BoundProperty? property)
    {
        string name;
        do
        {
            name = _dialect.ParameterName(_parameterCount++);
        }
        while (_givenParameterNames.Contains(name.TrimStart('@')));

        _parameters.Add(new QueryParameter(name, value, property));
        return name;
    }

    private string NextAlias() => _dialect.QuoteIdentifier(AliasPrefix + (_aliasCount++).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A selected column: its SQL, and its name in the rows, which is the column name unless renamed. A computed
    /// column, such as an aggregate, always needs its name given in a derived table.
    /// </summary>
    private readonly record struct SqlColumn(string Sql, string Name, bool IsComputed = false);

    /// <summary>A part of a group's key: its column, and the property it reads, nullable when its navigation can be missing.</summary>
    private sealed record GroupKey(SqlColumn Column, BoundProperty Value);

    private readonly record struct Ordering(SqlColumn Column, bool Descending);

    /// <summary>What a projection selects: property paths, then the entities at navigation paths.</summary>
    private sealed record Projection(IReadOnlyList<string> Columns, IReadOnlyList<string> EntityPaths);

    /// <summary>A table a navigation path reaches, and whether it has a row for every row of the query.</summary>
    private sealed record JoinTarget(string Alias, BoundEntity Entity, bool AlwaysExists);

    /// <summary>One SELECT level: a table or derived table, and the clauses applied to it so far.</summary>
    private sealed class SelectState(string source, string alias, bool isDerived = false)
    {
        public string Source { get; } = source;

        /// <summary>Whether the source is a derived table, the statement of the levels before this one.</summary>
        public bool IsDerived { get; } = isDerived;

        public string Alias { get; } = alias;

        public List<string> Joins { get; } = [];

        public List<string> Predicates { get; } = [];

        public List<Ordering> Ordering { get; } = [];

        public string? Offset { get; set; }

        public string? Limit { get; set; }

        public bool HasPaging => Offset is not null || Limit is not null;

        /// <summary>A projection not selected yet; until it is, the level's columns are the queried entity's.</summary>
        public Projection? Pending { get; set; }

        /// <summary>The columns a selected projection reads, or <see langword="null"/> for every entity column.</summary>
        public IReadOnlyList<SqlColumn>? Columns { get; set; }

        /// <summary>The key columns of a grouped level, or <see langword="null"/> when the rows are not grouped.</summary>
        public IReadOnlyList<GroupKey>? Keys { get; set; }

        /// <summary>The conditions on the groups of a grouped level.</summary>
        public List<string> Having { get; } = [];

        /// <summary>Whether the rows are distinct, which a later operator must not change.</summary>
        public bool Distinct { get; set; }

        /// <summary>The tables joined at this level, by navigation path from the queried entity.</summary>
        public Dictionary<string, JoinTarget> Targets { get; } = new(StringComparer.Ordinal);
    }
}
