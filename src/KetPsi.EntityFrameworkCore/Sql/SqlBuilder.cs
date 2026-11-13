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

    // The builder of the statement a subquery belongs to, which owns the aliases and parameters.
    private readonly SqlBuilder? _outer;
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

    // Whether the predicate or value being rendered reads the queried entity's paths, so a related collection can be
    // queried through them.
    private bool _readsCollections;

    // The properties whose converters read the statement's columns, set when the select list is final.
    private IReadOnlyList<BoundProperty?>? _converted;

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

    /// <summary>The builder of a subquery on <paramref name="entity"/>, the related entity of a collection navigation.</summary>
    private SqlBuilder(SqlBuilder outer, BoundEntity entity)
        : this(entity, outer._dialect, [], outer._filtersFor, outer._bindEntity, includes: null)
    {
        _outer = outer;
    }

    private SelectState State => _state ?? throw new UnreachableException("The chain did not start with a root node.");

    // The builder of the whole statement.
    private SqlBuilder Statement => _outer?.Statement ?? this;

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
            var converted = builder._readsFirstColumn ? null : Converted(entity.Properties.Select(static p => p.Converter is null ? null : p));
            return new SqlStatement(builder._rootSql!, builder._parameters, byName, converted);
        }

        var commandText = builder.Finish(terminal, rowLimit, aggregate);
        return new SqlStatement(commandText, builder._parameters, ConvertedColumns: builder._converted);
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
                AddOrdering(order);
                break;
            case OrderNode order:
                EnsureEntityRows();
                AddOrdering(order);
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
    /// Orders by the node's property path or computed key. A key that does not read the row, such as a constant or a
    /// captured variable, orders nothing, and EF Core leaves it out.
    /// </summary>
    private void AddOrdering(OrderNode order)
    {
        if (order.Key is null)
        {
            State.Ordering.Add(new Ordering(ResolvePath(order.PropertyName!).Column, order.Descending));
            return;
        }

        var key = RenderEntityValue(order.Key, order.Values);
        if (key.ReadsRow)
        {
            State.Ordering.Add(new Ordering(Computed(key.Sql), order.Descending));
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
        ValueComparisonTemplate comparison => new ValueComparisonTemplate(
            Prefixed(comparison.Left, path), comparison.Comparison, Prefixed(comparison.Right, path)),
        BooleanValueTemplate boolean => new BooleanValueTemplate(Prefixed(boolean.Value, path)),
        StringMatchTemplate match => new StringMatchTemplate(match.Match, Prefixed(match.Value, path), Prefixed(match.Part, path)),
        LikeTemplate like => new LikeTemplate(
            Prefixed(like.Value, path), Prefixed(like.Pattern, path), like.Escape is null ? null : Prefixed(like.Escape, path)),
        NullOrEmptyTemplate empty => new NullOrEmptyTemplate(Prefixed(empty.Value, path), empty.OrWhiteSpace),
        ExistsTemplate => throw new NotSupportedException(ExceptionMessages.CollectionQueryNotSupportedHere),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    /// <summary>The value with each name read through <paramref name="path"/>.</summary>
    private static ValueTemplate Prefixed(ValueTemplate template, string path) => template switch
    {
        PropertyValueTemplate property => new PropertyValueTemplate(path + "." + property.PropertyName),
        SlotValueTemplate => template,
        BinaryValueTemplate binary => new BinaryValueTemplate(binary.Operator, Prefixed(binary.Left, path), Prefixed(binary.Right, path)),
        UnaryValueTemplate unary => new UnaryValueTemplate(unary.Operator, Prefixed(unary.Operand, path)),
        ConvertValueTemplate convert => new ConvertValueTemplate(Prefixed(convert.Operand, path), convert.Type),
        CoalesceValueTemplate coalesce => new CoalesceValueTemplate(Prefixed(coalesce.Left, path), Prefixed(coalesce.Right, path)),
        ConditionalValueTemplate conditional => new ConditionalValueTemplate(
            Prefixed(conditional.Test, path), Prefixed(conditional.WhenTrue, path), Prefixed(conditional.WhenFalse, path)),
        PredicateValueTemplate predicate => new PredicateValueTemplate(Prefixed(predicate.Predicate, path)),
        FunctionValueTemplate function => new FunctionValueTemplate(function.Function, [.. function.Arguments.Select(a => Prefixed(a, path))]),
        CountValueTemplate or CollectionAggregateTemplate => throw new NotSupportedException(ExceptionMessages.CollectionQueryNotSupportedHere),
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
                // Min and Max return a column value, which the converter reads, as in EF Core.
                var converted = value.Function is AggregateFunction.Min or AggregateFunction.Max && property.Converter is not null ? property : null;
                return (Computed(aggregate) with { Converted = converted }, AggregateValue(value, property));

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
                _converted = ConvertedColumns(State);
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

                // Min and Max return a column value, which the converter reads, as in EF Core.
                if (aggregate!.Function is AggregateFunction.Min or AggregateFunction.Max && column.Converted is not null)
                {
                    _converted = [column.Converted];
                }

                return RenderSelect(State, _dialect.Aggregate(aggregate.Function, column.Sql, aggregate.ValueType), includeOrdering: false);

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
        _converted = ConvertedColumns(State);
        return RenderSelect(State, SelectList(State), includeOrdering: true);
    }

    // The converted properties of the columns a select list reads: the projected columns, or every entity column.
    private IReadOnlyList<BoundProperty?>? ConvertedColumns(SelectState state)
        => Converted(state.Columns is { } columns
            ? columns.Select(static c => c.Converted)
            : _entity.Properties.Select(static p => p.Converter is null ? null : p));

    private static IReadOnlyList<BoundProperty?>? Converted(IEnumerable<BoundProperty?> columns)
    {
        var list = columns.ToList();
        return list.Exists(static p => p is not null) ? list : null;
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
        => new(new SqlStatement(commandText, _parameters, ConvertedColumns: _converted), _segments, _collections, _roots);

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

        var (resolve, column) = PathColumns();
        var predicate = PredicateBinder.Bind(ExpandNavigationChecks(template), resolve, _entity.ClrType);
        var reads = _readsCollections;
        _readsCollections = true;
        try
        {
            return RenderPredicate(predicate, values, negated: false, column);
        }
        finally
        {
            _readsCollections = reads;
        }
    }

    /// <summary>A computed value of the queried entity at the current level, such as an order key; its names are property paths.</summary>
    private SqlValue RenderEntityValue(ValueTemplate template, IReadOnlyList<object?> values)
    {
        if (ValueNames(template).All(_entity.HasProperty))
        {
            return RenderValue(_entity.BindValue(template), values, ColumnsAt(State.Alias), hint: null);
        }

        var (resolve, column) = PathColumns();
        var value = PredicateBinder.Bind(ExpandNavigationChecks(template), resolve, _entity.ClrType);
        var reads = _readsCollections;
        _readsCollections = true;
        try
        {
            return RenderValue(value, values, column, hint: null);
        }
        finally
        {
            _readsCollections = reads;
        }
    }

    /// <summary>Binds property paths to the columns they read, joining the navigations they go through.</summary>
    private (Func<string, BoundProperty> Resolve, Func<BoundProperty, string> Column) PathColumns()
    {
        // Each use gets its own instance, so the same path read twice maps to its column either way.
        var columns = new Dictionary<BoundProperty, string>(ReferenceEqualityComparer.Instance);
        BoundProperty Resolve(string path)
        {
            var (column, property) = ResolvePath(path);
            var operand = property with { };
            columns[operand] = column.Sql;
            return operand;
        }

        return (Resolve, property => columns[property]);
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
        ValueComparisonTemplate comparison => ValueNames(comparison.Left).Concat(ValueNames(comparison.Right)),
        BooleanValueTemplate boolean => ValueNames(boolean.Value),
        StringMatchTemplate match => ValueNames(match.Value).Concat(ValueNames(match.Part)),
        LikeTemplate like => ValueNames(like.Value).Concat(ValueNames(like.Pattern)).Concat(like.Escape is null ? [] : ValueNames(like.Escape)),
        NullOrEmptyTemplate empty => ValueNames(empty.Value),
        ExistsTemplate exists => [exists.Collection.NavigationPath],
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    private static IEnumerable<string> ValueNames(ValueTemplate template) => template switch
    {
        PropertyValueTemplate property => [property.PropertyName],
        SlotValueTemplate => [],
        BinaryValueTemplate binary => ValueNames(binary.Left).Concat(ValueNames(binary.Right)),
        UnaryValueTemplate unary => ValueNames(unary.Operand),
        ConvertValueTemplate convert => ValueNames(convert.Operand),
        CoalesceValueTemplate coalesce => ValueNames(coalesce.Left).Concat(ValueNames(coalesce.Right)),
        ConditionalValueTemplate conditional => TemplateNames(conditional.Test)
            .Concat(ValueNames(conditional.WhenTrue)).Concat(ValueNames(conditional.WhenFalse)),
        PredicateValueTemplate predicate => TemplateNames(predicate.Predicate),
        FunctionValueTemplate function => function.Arguments.SelectMany(ValueNames),
        CountValueTemplate count => [count.Collection.NavigationPath],
        CollectionAggregateTemplate aggregate => [aggregate.Collection.NavigationPath],
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
            case ValueComparisonTemplate comparison:
                var (left, right) = (ExpandNavigationChecks(comparison.Left), ExpandNavigationChecks(comparison.Right));
                return ReferenceEquals(left, comparison.Left) && ReferenceEquals(right, comparison.Right)
                    ? comparison
                    : new ValueComparisonTemplate(left, comparison.Comparison, right);
            case BooleanValueTemplate boolean:
                var value = ExpandNavigationChecks(boolean.Value);
                return ReferenceEquals(value, boolean.Value) ? boolean : new BooleanValueTemplate(value);
            default:
                return template;
        }
    }

    /// <summary>The value with the navigation null checks of its conditions expanded.</summary>
    private ValueTemplate ExpandNavigationChecks(ValueTemplate template)
    {
        switch (template)
        {
            case BinaryValueTemplate binary:
                var (left, right) = (ExpandNavigationChecks(binary.Left), ExpandNavigationChecks(binary.Right));
                return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                    ? binary
                    : new BinaryValueTemplate(binary.Operator, left, right);
            case UnaryValueTemplate unary:
                var operand = ExpandNavigationChecks(unary.Operand);
                return ReferenceEquals(operand, unary.Operand) ? unary : new UnaryValueTemplate(unary.Operator, operand);
            case ConvertValueTemplate convert:
                var converted = ExpandNavigationChecks(convert.Operand);
                return ReferenceEquals(converted, convert.Operand) ? convert : new ConvertValueTemplate(converted, convert.Type);
            case CoalesceValueTemplate coalesce:
                var (first, second) = (ExpandNavigationChecks(coalesce.Left), ExpandNavigationChecks(coalesce.Right));
                return ReferenceEquals(first, coalesce.Left) && ReferenceEquals(second, coalesce.Right)
                    ? coalesce
                    : new CoalesceValueTemplate(first, second);
            case ConditionalValueTemplate conditional:
                var test = ExpandNavigationChecks(conditional.Test);
                var (whenTrue, whenFalse) = (ExpandNavigationChecks(conditional.WhenTrue), ExpandNavigationChecks(conditional.WhenFalse));
                return ReferenceEquals(test, conditional.Test) && ReferenceEquals(whenTrue, conditional.WhenTrue)
                    && ReferenceEquals(whenFalse, conditional.WhenFalse)
                    ? conditional
                    : new ConditionalValueTemplate(test, whenTrue, whenFalse);
            case PredicateValueTemplate predicate:
                var expanded = ExpandNavigationChecks(predicate.Predicate);
                return ReferenceEquals(expanded, predicate.Predicate) ? predicate : new PredicateValueTemplate(expanded);
            default:
                return template;
        }
    }

    /// <summary>The navigation a path ends with and the path of the entity that has it, or null when it ends with a property.</summary>
    private (string ParentPath, BoundNavigation Navigation)? NavigationAt(string path)
    {
        if (IsOuterName(path))
        {
            return OuterBuilder(path).NavigationAt(path[1..]);
        }

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
        if (IsOuterName(path))
        {
            return OuterBuilder(path).ResolvePath(path[1..]);
        }

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
            var list = AddParameter(_dialect.CreateListParameterValue(parentKeys.Select(k => columns[0].ToProvider(k[0]))), property: null);
            return _dialect.InList(Column(columns[0], State.Alias), list, columns[0].StoreType);
        }

        var converted = columns.Any(static c => c.Converter is not null)
            ? parentKeys.Select(k => (IReadOnlyList<object?>)k.Select((v, i) => columns[i].ToProvider(v)).ToArray())
            : parentKeys;
        var tuples = AddParameter(_dialect.CreateTupleListParameterValue(converted), property: null);
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
        SqlColumn Outer(int index) => new(outer.Alias + "." + _dialect.QuoteIdentifier(names[index]), names[index], Converted: selected[index].Converted);
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
        // A converted bool, such as one stored as 'Y' or 'N', is compared with the stored value of true or false.
        BoundBooleanProperty { Property.Converter: not null } boolean => column(boolean.Property) + " = " + AddParameter(!negated, boolean.Property),
        BoundBooleanProperty boolean =>
            column(boolean.Property) + " = " + (negated ? _dialect.FalseLiteral : _dialect.TrueLiteral),
        BoundNullCheck nullCheck => column(nullCheck.Property) + (nullCheck.IsNull != negated ? " IS NULL" : " IS NOT NULL"),
        BoundInList inList => RenderInList(inList, values[inList.ValueIndex], negated, column),
        BoundValueComparison comparison => RenderValueComparison(comparison, values, negated, column),
        BoundBooleanValue boolean => RenderBooleanValue(boolean, values, negated, column),
        BoundStringMatch match => RenderStringMatch(match, values, negated, column),
        BoundLike like => RenderLike(like, values, negated, column),
        BoundNullOrEmpty empty => RenderNullOrEmpty(empty, values, negated, column),
        BoundExists exists => (negated ? "NOT EXISTS (" : "EXISTS (") + RenderSubquery(exists.Collection, values, static _ => ExistsProjection) + ")",
        _ => throw new UnreachableException(predicate.GetType().ToString()),
    };

    /// <summary>
    /// <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c>, as EF Core writes them: a value is sent as a <c>LIKE</c>
    /// pattern with its wildcards escaped; a column's value is searched for, after checking neither side is null.
    /// Looking for null is false, as EF Core writes it.
    /// </summary>
    private string RenderStringMatch(BoundStringMatch match, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column)
    {
        var value = RenderValue(match.Value, values, column, hint: null);
        if (match.Part is BoundSlotValue slot)
        {
            if (values[slot.ValueIndex] is not { } part || value.IsNull)
            {
                return negated ? AlwaysTrue : AlwaysFalse;
            }

            var text = _dialect.EscapeLikePattern(System.Convert.ToString(part, CultureInfo.InvariantCulture)!);
            var pattern = match.Match switch
            {
                StringMatch.Contains => "%" + text + "%",
                StringMatch.StartsWith => text + "%",
                _ => "%" + text,
            };

            var like = _dialect.Like(value.Sql, AddParameter(pattern, property: null), _dialect.LikeEscapeLiteral);
            return negated ? NotLike(like, value) : like;
        }

        var searched = RenderValue(match.Part, values, column, hint: null);
        if (searched.IsNull || value.IsNull)
        {
            return negated ? AlwaysTrue : AlwaysFalse;
        }

        var core = match.Match switch
        {
            // CHARINDEX finds no empty string, which every string contains.
            StringMatch.Contains => Combine(
                "OR",
                StringFunction(SqlStringFunction.Position, searched.Sql, value.Sql) + " > 0",
                _dialect.Like(searched.Sql, _dialect.EmptyStringLiteral, escape: null)),
            StringMatch.StartsWith => StringFunction(SqlStringFunction.Left, value.Sql, StringFunction(SqlStringFunction.Length, searched.Sql)) + " = " + searched.Sql,
            _ => StringFunction(SqlStringFunction.Right, value.Sql, StringFunction(SqlStringFunction.Length, searched.Sql)) + " = " + searched.Sql,
        };

        // With neither side null the test is true or false, never unknown, so NOT negates it exactly.
        var parts = new List<string>();
        if (value.IsNullable)
        {
            parts.Add(Parenthesized(value) + " IS NOT NULL");
        }

        if (searched.IsNullable)
        {
            parts.Add(Parenthesized(searched) + " IS NOT NULL");
        }

        parts.Add(core);
        var positive = parts.Count == 1 ? core : "(" + string.Join(" AND ", parts) + ")";
        return negated ? "NOT " + (parts.Count == 1 && match.Match != StringMatch.Contains ? "(" + positive + ")" : positive) : positive;
    }

    // NOT LIKE is unknown for a null, which C#'s negation counts as true.
    private static string NotLike(string like, SqlValue value)
        => value.IsNullable ? Combine("OR", "NOT (" + like + ")", Parenthesized(value) + " IS NULL") : "NOT (" + like + ")";

    /// <summary><c>EF.Functions.Like</c>: false for a null pattern, as EF Core writes it; its negation keeps the nulls.</summary>
    private string RenderLike(BoundLike like, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column)
    {
        var value = RenderValue(like.Value, values, column, hint: null);
        var pattern = RenderValue(like.Pattern, values, column, hint: null);
        var escape = like.Escape is null ? (SqlValue?)null : RenderValue(like.Escape, values, column, hint: null);
        if (value.IsNull || pattern.IsNull || escape is { IsNull: true })
        {
            return negated ? AlwaysTrue : AlwaysFalse;
        }

        var sql = _dialect.Like(value.Sql, pattern.Sql, escape?.Sql);
        if (!negated)
        {
            return sql;
        }

        var parts = new List<string> { "NOT (" + sql + ")" };
        if (value.IsNullable)
        {
            parts.Add(Parenthesized(value) + " IS NULL");
        }

        if (pattern.IsNullable)
        {
            parts.Add(Parenthesized(pattern) + " IS NULL");
        }

        return parts.Count == 1 ? parts[0] : "(" + string.Join(" OR ", parts) + ")";
    }

    /// <summary><c>string.IsNullOrEmpty</c> and <c>IsNullOrWhiteSpace</c>, as EF Core writes them.</summary>
    private string RenderNullOrEmpty(BoundNullOrEmpty empty, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column)
    {
        var value = RenderValue(empty.Value, values, column, hint: null);
        if (value.IsNull)
        {
            return negated ? AlwaysFalse : AlwaysTrue;
        }

        // SQL Server's = ignores trailing spaces, so a string of spaces equals the empty one; LIKE does not.
        var isEmpty = empty.OrWhiteSpace
            ? value.Sql + " = " + _dialect.EmptyStringLiteral
            : _dialect.Like(value.Sql, _dialect.EmptyStringLiteral, escape: null);
        if (!value.IsNullable)
        {
            return negated ? "NOT (" + isEmpty + ")" : isEmpty;
        }

        return negated
            ? Combine("AND", Parenthesized(value) + " IS NOT NULL", "NOT (" + isEmpty + ")")
            : Combine("OR", Parenthesized(value) + " IS NULL", isEmpty);
    }

    private string StringFunction(SqlStringFunction function, params string[] arguments) => _dialect.StringFunction(function, arguments);

    /// <summary>
    /// A string method or member, as EF Core writes it. A null string makes the result null, as EF Core's SQL does
    /// where C# would throw.
    /// </summary>
    private SqlValue RenderFunction(BoundFunctionValue function, IReadOnlyList<object?> values, Func<BoundProperty, string> column)
    {
        var arguments = new List<SqlValue>(function.Arguments.Count);
        foreach (var argument in function.Arguments)
        {
            var rendered = RenderValue(argument, values, column, hint: null);
            if (rendered.IsNull && function.Function != ValueFunction.ToString)
            {
                return SqlValue.Null;
            }

            arguments.Add(rendered);
        }

        var value = arguments[0];
        var isNullable = arguments.Exists(static a => a.IsNullable);
        var readsRow = arguments.Exists(static a => a.ReadsRow);
        SqlValue Text(string sql) => new(sql, isNullable, typeof(string), readsRow);
        SqlValue Number(string sql, bool isCompound = false) => new(sql, isNullable, typeof(int), readsRow, isCompound);

        switch (function.Function)
        {
            case ValueFunction.ToUpper:
                return Text(StringFunction(SqlStringFunction.Upper, value.Sql));
            case ValueFunction.ToLower:
                return Text(StringFunction(SqlStringFunction.Lower, value.Sql));
            case ValueFunction.Trim:
                return Text(StringFunction(SqlStringFunction.TrimStart, StringFunction(SqlStringFunction.TrimEnd, value.Sql)));
            case ValueFunction.TrimStart:
                return Text(StringFunction(SqlStringFunction.TrimStart, value.Sql));
            case ValueFunction.TrimEnd:
                return Text(StringFunction(SqlStringFunction.TrimEnd, value.Sql));
            case ValueFunction.Length:
                return Number(StringFunction(SqlStringFunction.Length, value.Sql));
            case ValueFunction.Substring:
                // SQL counts from one; without a length, the rest of the string.
                var length = arguments.Count > 2 ? arguments[2].Sql : StringFunction(SqlStringFunction.Length, value.Sql);
                return Text(StringFunction(SqlStringFunction.Substring, value.Sql, Parenthesized(arguments[1]) + " + 1", length));
            case ValueFunction.Replace:
                return Text(StringFunction(SqlStringFunction.Replace, value.Sql, arguments[1].Sql, arguments[2].Sql));
            case ValueFunction.IndexOf:
                // SQL counts from one and finds no empty string; C# finds it at 0.
                var position = StringFunction(SqlStringFunction.Position, arguments[1].Sql, value.Sql);
                var offset = function.Arguments[1] is BoundSlotValue slot
                    ? values[slot.ValueIndex] is string { Length: 0 } ? "0" : "1"
                    : "CASE WHEN " + arguments[1].Sql + " = " + _dialect.EmptyStringLiteral + " THEN 0 ELSE 1 END";
                return Number(position + " - " + offset, isCompound: true);
            case ValueFunction.ToString when value.IsNull:
                return new SqlValue(_dialect.EmptyStringLiteral, IsNullable: false, typeof(string), ReadsRow: false);
            case ValueFunction.ToString when value.ClrType == typeof(string):
                return value;
            case ValueFunction.ToString when value.ClrType is { } type && (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)):
                // A null number's ToString is the empty string.
                var text = _dialect.Cast(value.Sql, typeof(string));
                return new SqlValue(
                    value.IsNullable ? "COALESCE(" + text + ", " + _dialect.EmptyStringLiteral + ")" : text, IsNullable: false, typeof(string), readsRow);
            case ValueFunction.ToString:
                throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, ExceptionMessages.ToStringNotSupported, value.ClrType));
            default:
                throw new UnreachableException(function.Function.ToString());
        }
    }

    /// <summary>
    /// A comparison of two computed values, true exactly where C# is: two nulls are equal, a null and a value are not,
    /// and an ordering comparison with a null is false, so its negation is true. A value compared with a property is
    /// sent typed by the property's column, and converted by its value converter.
    /// </summary>
    private string RenderValueComparison(BoundValueComparison comparison, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column)
    {
        var left = RenderValue(comparison.Left, values, column, DirectMapping(comparison.Right));
        var right = RenderValue(comparison.Right, values, column, DirectMapping(comparison.Left));
        var effective = negated ? Negate(comparison.Comparison) : comparison.Comparison;

        if (left.IsNull && right.IsNull)
        {
            // C# lifts two nulls: == is true, every other operator false.
            return comparison.Comparison == ComparisonOperator.Equal != negated ? AlwaysTrue : AlwaysFalse;
        }

        if (left.IsNull || right.IsNull)
        {
            // A comparison with null: == and != test for null, every ordering comparison is false, as in C#.
            var other = left.IsNull ? right : left;
            return effective switch
            {
                ComparisonOperator.Equal => other.IsNullable ? Parenthesized(other) + " IS NULL" : AlwaysFalse,
                ComparisonOperator.NotEqual => other.IsNullable ? Parenthesized(other) + " IS NOT NULL" : AlwaysTrue,
                _ => negated ? AlwaysTrue : AlwaysFalse,
            };
        }

        var sql = left.Sql + " " + OperatorSql(effective) + " " + right.Sql;
        switch (effective)
        {
            case ComparisonOperator.Equal:
                // NULL = NULL is unknown in SQL, but two nulls are equal in C#.
                return left.IsNullable && right.IsNullable
                    ? Combine("OR", sql, Combine("AND", Parenthesized(left) + " IS NULL", Parenthesized(right) + " IS NULL"))
                    : sql;
            case ComparisonOperator.NotEqual:
                // A null and a value differ in C#; two nulls do not.
                if (left.IsNullable && right.IsNullable)
                {
                    var differ = "(" + sql + " OR " + Parenthesized(left) + " IS NULL OR " + Parenthesized(right) + " IS NULL)";
                    var notBothNull = Combine("OR", Parenthesized(left) + " IS NOT NULL", Parenthesized(right) + " IS NOT NULL");
                    return Combine("AND", differ, notBothNull);
                }

                return left.IsNullable || right.IsNullable
                    ? Combine("OR", sql, Parenthesized(left.IsNullable ? left : right) + " IS NULL")
                    : sql;
            default:
                // An ordering comparison with a null is false in C#, so its negation is true.
                if (!negated || !left.IsNullable && !right.IsNullable)
                {
                    return sql;
                }

                var parts = new List<string> { sql };
                if (left.IsNullable)
                {
                    parts.Add(Parenthesized(left) + " IS NULL");
                }

                if (right.IsNullable)
                {
                    parts.Add(Parenthesized(right) + " IS NULL");
                }

                return "(" + string.Join(" OR ", parts) + ")";
        }
    }

    /// <summary>
    /// <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c> of the related rows, as EF Core writes them: <c>Sum</c> of no rows
    /// is zero, and the others are null, even when the selector is never null.
    /// </summary>
    private SqlValue RenderCollectionAggregate(CollectionAggregateTemplate template, IReadOnlyList<object?> values)
    {
        SqlValue selector = default;
        var sql = RenderSubquery(template.Collection, values, inner =>
        {
            selector = inner.RenderEntityValue(template.Selector, values);
            return _dialect.Aggregate(template.Function, selector.Sql, template.ValueType);
        });

        var type = (template.Function, template.ValueType) switch
        {
            (AggregateFunction.Min or AggregateFunction.Max, _) => selector.ClrType,
            (AggregateFunction.Average, AggregateValueType.Int32 or AggregateValueType.Int64 or AggregateValueType.Double) => typeof(double),
            (_, AggregateValueType.Int32) => typeof(int),
            (_, AggregateValueType.Int64) => typeof(long),
            (_, AggregateValueType.Double) => typeof(double),
            (_, AggregateValueType.Single) => typeof(float),
            (_, AggregateValueType.Decimal) => typeof(decimal),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template.ValueType, ExceptionMessages.GroupAggregateNeedsNumericType),
        };

        return new SqlValue("(" + sql + ")", IsNullable: template.Function != AggregateFunction.Sum, type, ReadsRow: true);
    }

    /// <summary>
    /// The correlated subquery on the related rows of a collection navigation: their table with its query filters,
    /// matched to the row the navigation path starts from by the navigation's keys, and filtered by the collection's
    /// condition, as EF Core writes it.
    /// </summary>
    /// <param name="collection">The collection.</param>
    /// <param name="values">The value slots.</param>
    /// <param name="projection">Renders what the subquery selects, with the builder of the subquery.</param>
    /// <exception cref="NotSupportedException">The collection is read outside a predicate or an ordering of the queried entity.</exception>
    private string RenderSubquery(CollectionTemplate collection, IReadOnlyList<object?> values, Func<SqlBuilder, string> projection)
    {
        if (!_readsCollections || IsOuterName(collection.NavigationPath))
        {
            throw new NotSupportedException(ExceptionMessages.CollectionQueryNotSupportedHere);
        }

        var path = collection.NavigationPath;
        var dot = path.LastIndexOf('.');
        var parent = GetTarget(dot < 0 ? RootPath : path[..dot], entity: null);
        var name = path[(dot + 1)..];
        var navigation = parent.Entity.GetNavigation(name);
        if (!navigation.IsCollection)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.NavigationNotCollection, parent.Entity.ClrType, name));
        }

        var target = Bind(navigation.TargetClrType);
        var inner = new SqlBuilder(this, target);
        var state = inner._state = new SelectState(_dialect.QualifyTable(target.Table, target.Schema), NextAlias());
        foreach (var filter in _filtersFor(target))
        {
            state.Predicates.Add(inner.RenderPredicate(target.BindPredicate(filter.Predicate), filter.Values, negated: false, state.Alias));
        }

        for (var i = 0; i < navigation.SourceKey.Count; i++)
        {
            state.Predicates.Add(
                Column(navigation.SourceKey[i], parent.Alias) + " = " + Column(target.GetProperty(navigation.TargetKey[i]), state.Alias));
        }

        if (collection.Predicate is { } predicate)
        {
            state.Predicates.Add(inner.RenderEntityPredicate(predicate, values));
        }

        var select = projection(inner);
        return "SELECT " + select + " FROM " + RenderFrom(state);
    }

    /// <summary>Whether a name reads the row a collection navigation starts from, such as <c>^MinRating</c>.</summary>
    private static bool IsOuterName(string name) => name.StartsWith('^');

    private SqlBuilder OuterBuilder(string name)
        => _outer ?? throw new InvalidOperationException(
            string.Format(CultureInfo.InvariantCulture, ExceptionMessages.OuterNameWithoutCollection, name));

    /// <summary>A computed <see cref="bool"/> used as a predicate: true where it is true, and its negation where it is false or null.</summary>
    private string RenderBooleanValue(BoundBooleanValue boolean, IReadOnlyList<object?> values, bool negated, Func<BoundProperty, string> column)
    {
        var value = RenderValue(boolean.Value, values, column, hint: null);
        if (value.IsNull)
        {
            return negated ? AlwaysTrue : AlwaysFalse;
        }

        if (!negated)
        {
            return value.Sql + " = " + _dialect.TrueLiteral;
        }

        var isFalse = value.Sql + " = " + _dialect.FalseLiteral;
        return value.IsNullable ? Combine("OR", isFalse, Parenthesized(value) + " IS NULL") : isFalse;
    }

    /// <summary>
    /// The SQL of a computed value, with what the comparisons around it need to know: whether it can be null, and its
    /// CLR type, which decides the casts of C#'s conversions.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="values">The value slots.</param>
    /// <param name="column">The SQL of a bound property's column.</param>
    /// <param name="hint">The property a slot is compared or combined with, which types its parameter, or <see langword="null"/>.</param>
    private SqlValue RenderValue(BoundValue value, IReadOnlyList<object?> values, Func<BoundProperty, string> column, BoundProperty? hint)
    {
        switch (value)
        {
            case BoundPropertyValue { Property: var property }:
                return new SqlValue(column(property), property.IsNullable, Underlying(property.ClrType), ReadsRow: true);
            case BoundSlotValue slot:
                // A character is a string of one in SQL, as EF Core sends it.
                return values[slot.ValueIndex] is { } slotValue
                    ? slotValue is char character
                        ? new SqlValue(AddParameter(character.ToString(), property: null), IsNullable: false, typeof(string), ReadsRow: false)
                        : new SqlValue(AddParameter(slotValue, Fits(hint, slotValue) ? hint : null), IsNullable: false, slotValue.GetType(), ReadsRow: false)
                    : SqlValue.Null;
            case BoundBinaryValue binary:
                return RenderBinary(binary, values, column);
            case BoundUnaryValue unary:
            {
                var operand = RenderValue(unary.Operand, values, column, hint);
                if (operand.IsNull)
                {
                    return SqlValue.Null;
                }

                var op = unary.Operator == UnaryValueOperator.Negate ? "-" : "~";
                return operand with { Sql = op + Parenthesized(operand), IsCompound = false };
            }

            case BoundConvertValue convert:
            {
                var operand = RenderValue(convert.Operand, values, column, hint: null);
                if (operand.IsNull)
                {
                    return SqlValue.Null;
                }

                var sql = NeedsCast(operand.ClrType, convert.Type) ? _dialect.Cast(operand.Sql, convert.Type) : operand.Sql;
                return operand with { Sql = sql, ClrType = convert.Type, IsCompound = operand.IsCompound && sql == operand.Sql };
            }

            case BoundCoalesceValue coalesce:
            {
                // A left side that is never null is the value; a null one is the right side.
                var left = RenderValue(coalesce.Left, values, column, PlainMapping(coalesce.Right) ?? hint);
                if (!left.IsNullable)
                {
                    return left;
                }

                var right = RenderValue(coalesce.Right, values, column, PlainMapping(coalesce.Left) ?? hint);
                if (left.IsNull)
                {
                    return right;
                }

                return new SqlValue(
                    "COALESCE(" + left.Sql + ", " + right.Sql + ")",
                    right.IsNull || right.IsNullable,
                    left.ClrType ?? right.ClrType,
                    left.ReadsRow || right.ReadsRow);
            }

            case BoundConditionalValue conditional:
            {
                var test = RenderPredicate(conditional.Test, values, negated: false, column);
                var whenTrue = RenderValue(conditional.WhenTrue, values, column, PlainMapping(conditional.WhenFalse) ?? hint);
                var whenFalse = RenderValue(conditional.WhenFalse, values, column, PlainMapping(conditional.WhenTrue) ?? hint);
                return new SqlValue(
                    "CASE WHEN " + test + " THEN " + whenTrue.Sql + " ELSE " + whenFalse.Sql + " END",
                    whenTrue.IsNull || whenTrue.IsNullable || whenFalse.IsNull || whenFalse.IsNullable,
                    whenTrue.ClrType ?? whenFalse.ClrType,
                    ReadsRow: true);
            }

            case BoundFunctionValue function:
                return RenderFunction(function, values, column);

            case BoundCountValue count:
                return new SqlValue(
                    "(" + RenderSubquery(count.Collection, values, _ => _dialect.Count(count.IsLong)) + ")",
                    IsNullable: false,
                    count.IsLong ? typeof(long) : typeof(int),
                    ReadsRow: true);

            case BoundCollectionAggregate aggregate:
                return RenderCollectionAggregate(aggregate.Template, values);

            case BoundPredicateValue predicate:
            {
                var test = RenderPredicate(predicate.Predicate, values, negated: false, column);
                return new SqlValue(
                    "CASE WHEN " + test + " THEN " + _dialect.TrueLiteral + " ELSE " + _dialect.FalseLiteral + " END",
                    IsNullable: false,
                    typeof(bool),
                    ReadsRow: true);
            }

            default:
                throw new UnreachableException(value.GetType().ToString());
        }
    }

    private SqlValue RenderBinary(BoundBinaryValue binary, IReadOnlyList<object?> values, Func<BoundProperty, string> column)
    {
        if (binary.Operator == ValueOperator.Concat)
        {
            // C# concatenates a null string as an empty one, so the result is never null.
            var first = RenderValue(binary.Left, values, column, hint: null);
            var second = RenderValue(binary.Right, values, column, hint: null);
            return new SqlValue(
                _dialect.Concat(NotNullString(first), NotNullString(second)),
                IsNullable: false,
                typeof(string),
                first.ReadsRow || second.ReadsRow,
                IsCompound: true);
        }

        // C# lifts an operator with a null operand to null.
        var left = RenderValue(binary.Left, values, column, PlainMapping(binary.Right));
        if (left.IsNull)
        {
            return SqlValue.Null;
        }

        var right = RenderValue(binary.Right, values, column, PlainMapping(binary.Left));
        if (right.IsNull)
        {
            return SqlValue.Null;
        }

        var op = binary.Operator switch
        {
            ValueOperator.Add => "+",
            ValueOperator.Subtract => "-",
            ValueOperator.Multiply => "*",
            ValueOperator.Divide => "/",
            ValueOperator.Modulo => "%",
            ValueOperator.BitwiseAnd => "&",
            ValueOperator.BitwiseOr => "|",
            ValueOperator.ExclusiveOr => "^",
            _ => throw new UnreachableException(binary.Operator.ToString()),
        };

        return new SqlValue(
            Parenthesized(left) + " " + op + " " + Parenthesized(right),
            left.IsNullable || right.IsNullable,
            left.ClrType ?? right.ClrType,
            left.ReadsRow || right.ReadsRow,
            IsCompound: true);
    }

    private string NotNullString(SqlValue value)
        => value.IsNull
            ? _dialect.EmptyStringLiteral
            : value.IsNullable ? "COALESCE(" + value.Sql + ", " + _dialect.EmptyStringLiteral + ")" : Parenthesized(value);

    private static string Parenthesized(SqlValue value) => value.IsCompound ? "(" + value.Sql + ")" : value.Sql;

    /// <summary>
    /// Whether SQL needs a <c>CAST</c> for a C# conversion, as EF Core writes them: none to the same type, between an
    /// enum and its number, or widening a smaller integer to <see cref="int"/>, which SQL Server's arithmetic does itself.
    /// </summary>
    private static bool NeedsCast(Type? from, Type to)
    {
        if (from is null)
        {
            return !to.IsEnum;
        }

        if (from == to || from.IsEnum || to.IsEnum)
        {
            return false;
        }

        return !(to == typeof(int) && (from == typeof(byte) || from == typeof(sbyte) || from == typeof(short) || from == typeof(ushort)));
    }

    // The property a value is, with its converter, for a slot it is compared with.
    private static BoundProperty? DirectMapping(BoundValue value) => value is BoundPropertyValue { Property: var property } ? property : null;

    // The property a value is, for a slot combined with it in arithmetic; a converted property's stored type is not the operator's.
    private static BoundProperty? PlainMapping(BoundValue value)
        => value is BoundPropertyValue { Property: { Converter: null } property } ? property : null;

    // Whether a slot's value has the type of the property, so the property's column can type its parameter.
    private static bool Fits(BoundProperty? property, object value)
    {
        if (property is null)
        {
            return false;
        }

        var type = Underlying(property.ClrType);
        return type == value.GetType() || type.IsEnum && Enum.GetUnderlyingType(type) == value.GetType();
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

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
                nonNullValues.Add(inList.Property.ToProvider(item));
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
    private SqlColumn AggregatedColumn(SqlAggregate aggregate)
    {
        if (aggregate.PropertyName is { } path)
        {
            EnsureNotProjected();
            return ResolveColumn(path);
        }

        Materialize();
        if (_roots.Count > 0)
        {
            throw new InvalidOperationException(ExceptionMessages.AggregateNeedsColumnOnly);
        }

        return State.Columns is [var single]
            ? single
            : throw new InvalidOperationException(ExceptionMessages.AggregateNeedsOneColumn);
    }

    // The projected columns, or every entity column for an entity query.
    private string SelectList(SelectState state)
        => state.Columns is null ? EntityColumns(state.Alias) : string.Join(", ", state.Columns.Select(static c => c.Sql));

    private string EntityColumns(string alias) => string.Join(", ", _entity.Properties.Select(p => Column(p, alias)));

    private SqlColumn RootColumn(BoundProperty property, string alias)
        => new(Column(property, alias), property.ColumnName, Converted: property.Converter is null ? null : property);

    private string Column(BoundProperty property, string alias) => alias + "." + _dialect.QuoteIdentifier(property.ColumnName);

    private Func<BoundProperty, string> ColumnsAt(string alias) => property => Column(property, alias);

    private string AddParameter(object value, BoundProperty? property)
    {
        string name;
        do
        {
            name = _dialect.ParameterName(Statement._parameterCount++);
        }
        while (Statement._givenParameterNames.Contains(name.TrimStart('@')));

        // A value compared with a converted property is sent as the column holds it, as EF Core sends it.
        Statement._parameters.Add(new QueryParameter(name, property?.Converter is null ? value : property.ToProvider(value) ?? DBNull.Value, property));
        return name;
    }

    private string NextAlias() => _dialect.QuoteIdentifier(AliasPrefix + (Statement._aliasCount++).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A selected column: its SQL, and its name in the rows, which is the column name unless renamed. A computed
    /// column, such as an aggregate, always needs its name given in a derived table.
    /// </summary>
    /// <param name="Sql">The column's SQL.</param>
    /// <param name="Name">The column's name in the rows.</param>
    /// <param name="IsComputed">Whether the column is computed, such as an aggregate.</param>
    /// <param name="Converted">The property whose value converter the column's values go through, if any.</param>
    private readonly record struct SqlColumn(string Sql, string Name, bool IsComputed = false, BoundProperty? Converted = null);

    /// <summary>A computed value's SQL.</summary>
    /// <param name="Sql">The SQL.</param>
    /// <param name="IsNullable">Whether the value can be null.</param>
    /// <param name="ClrType">The value's CLR type, never nullable; <see langword="null"/> for the null literal.</param>
    /// <param name="ReadsRow">Whether the value reads a column, rather than only slots.</param>
    /// <param name="IsCompound">Whether the SQL is an operator expression, which an operand position must parenthesize.</param>
    /// <param name="IsNull">Whether the value is the null literal: a null slot, or an operator lifted to null.</param>
    private readonly record struct SqlValue(
        string Sql, bool IsNullable, Type? ClrType, bool ReadsRow, bool IsCompound = false, bool IsNull = false)
    {
        public static SqlValue Null { get; } = new("NULL", IsNullable: true, ClrType: null, ReadsRow: false, IsNull: true);
    }

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
