using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KetPsi.EntityFrameworkCore.Binding;
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
/// </remarks>
internal sealed class SqlBuilder
{
    private const string AliasPrefix = "t";
    private const string AlwaysTrue = "1 = 1";
    private const string AlwaysFalse = "0 = 1";
    private const string ExistsProjection = "1";

    private readonly BoundEntity _entity;
    private readonly ISqlDialect _dialect;
    private readonly List<QueryParameter> _parameters = [];
    private int _aliasCount;
    private SelectState? _state;

    private SqlBuilder(BoundEntity entity, ISqlDialect dialect)
    {
        _entity = entity;
        _dialect = dialect;
    }

    private SelectState State => _state ?? throw new UnreachableException("The chain did not start with a root node.");

    /// <summary>Builds the statement for the chain ending at <paramref name="tail"/>.</summary>
    /// <param name="entity">The queried entity, bound to the model.</param>
    /// <param name="tail">The last node of the chain.</param>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="terminal">What the statement returns.</param>
    /// <param name="rowLimit">For <see cref="QueryTerminal.Rows"/>, a limit applied after every operator, such as 1 for <c>First</c>.</param>
    public static SqlStatement Build(BoundEntity entity, QueryNode tail, ISqlDialect dialect, QueryTerminal terminal, int? rowLimit = null)
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

        var builder = new SqlBuilder(entity, dialect);
        foreach (var node in Chain(tail))
        {
            builder.Apply(node);
        }

        var commandText = builder.Finish(terminal, rowLimit);
        return new SqlStatement(commandText, builder._parameters);
    }

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
        switch (node)
        {
            case RootNode root:
                ApplyRoot(root);
                break;
            case WhereNode where:
                WrapIf(State.HasPaging);
                var predicate = _entity.BindPredicate(where.Predicate);
                State.Predicates.Add(RenderPredicate(predicate, where.Values, negated: false));
                break;
            case OrderNode { IsThenBy: false } order:
                WrapIf(State.HasPaging);
                State.Ordering.Clear();
                State.Ordering.Add(new Ordering(_entity.GetProperty(order.PropertyName), order.Descending));
                break;
            case OrderNode order:
                State.Ordering.Add(new Ordering(_entity.GetProperty(order.PropertyName), order.Descending));
                break;
            case SkipNode skip:
                WrapIf(State.HasPaging);
                State.Offset = AddParameter(skip.Count, property: null);
                break;
            case TakeNode take:
                WrapIf(State.Limit is not null);
                State.Limit = AddParameter(take.Count, property: null);
                break;
            case NoTrackingNode or IgnoreQueryFiltersNode:
                // Tracking and query filters don't change the SQL built here.
                break;
            default:
                throw new UnreachableException(node.GetType().ToString());
        }
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

        _state = new SelectState(_dialect.QualifyTable(_entity.Table, _entity.Schema), NextAlias());
    }

    private string Finish(QueryTerminal terminal, int? rowLimit)
    {
        switch (terminal)
        {
            case QueryTerminal.Rows:
                if (rowLimit is { } limit)
                {
                    WrapIf(State.Limit is not null);
                    State.Limit = limit.ToString(CultureInfo.InvariantCulture);
                }

                return RenderSelect(State, EntityColumns(State.Alias), includeOrdering: true);

            case QueryTerminal.Count or QueryTerminal.LongCount:
                WrapIf(State.HasPaging);
                return RenderSelect(State, _dialect.Count(terminal == QueryTerminal.LongCount), includeOrdering: false);

            case QueryTerminal.Any:
                // ORDER BY is only valid in a subquery that pages, and only matters there.
                return _dialect.Exists(RenderSelect(State, ExistsProjection, includeOrdering: State.HasPaging));

            default:
                throw new UnreachableException(terminal.ToString());
        }
    }

    private void WrapIf(bool condition)
    {
        if (!condition)
        {
            return;
        }

        var inner = State;
        var source = "(" + RenderSelect(inner, EntityColumns(inner.Alias), includeOrdering: true) + ")";
        var outer = new SelectState(source, NextAlias());
        outer.Ordering.AddRange(inner.Ordering);
        _state = outer;
    }

    private string RenderSelect(SelectState state, string projection, bool includeOrdering)
    {
        var hasOrderBy = includeOrdering && state.Ordering.Count > 0;
        var paging = _dialect.Paging(state.Offset, state.Limit, hasOrderBy);

        var sql = new StringBuilder("SELECT ");
        if (paging.SelectPrefix is not null)
        {
            sql.Append(paging.SelectPrefix).Append(' ');
        }

        sql.Append(projection).Append(" FROM ").Append(state.Source).Append(" AS ").Append(state.Alias);

        if (state.Predicates.Count > 0)
        {
            sql.Append(" WHERE ").AppendJoin(" AND ", state.Predicates);
        }

        if (hasOrderBy)
        {
            sql.Append(" ORDER BY ").AppendJoin(", ", state.Ordering.Select(o => RenderOrdering(o, state.Alias)));
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

    private string RenderOrdering(Ordering ordering, string alias)
        => ordering.Descending ? Column(ordering.Property, alias) + " DESC" : Column(ordering.Property, alias);

    private string RenderPredicate(BoundPredicate predicate, IReadOnlyList<object?> values, bool negated) => predicate switch
    {
        BoundAnd and => Combine(negated ? "OR" : "AND", RenderPredicate(and.Left, values, negated), RenderPredicate(and.Right, values, negated)),
        BoundOr or => Combine(negated ? "AND" : "OR", RenderPredicate(or.Left, values, negated), RenderPredicate(or.Right, values, negated)),
        BoundNot not => RenderPredicate(not.Operand, values, !negated),
        BoundComparison comparison => RenderComparison(comparison, values[comparison.ValueIndex], negated),
        BoundBooleanProperty boolean =>
            Column(boolean.Property) + " = " + (negated ? _dialect.FalseLiteral : _dialect.TrueLiteral),
        BoundNullCheck nullCheck => Column(nullCheck.Property) + (nullCheck.IsNull != negated ? " IS NULL" : " IS NOT NULL"),
        BoundInList inList => RenderInList(inList, values[inList.ValueIndex], negated),
        _ => throw new UnreachableException(predicate.GetType().ToString()),
    };

    private string RenderComparison(BoundComparison comparison, object? value, bool negated)
    {
        var column = Column(comparison.Property);
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

    private string RenderInList(BoundInList inList, object? value, bool negated)
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

        var column = Column(inList.Property);
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

    private string EntityColumns(string alias) => string.Join(", ", _entity.Properties.Select(p => Column(p, alias)));

    private string Column(BoundProperty property) => Column(property, State.Alias);

    private string Column(BoundProperty property, string alias) => alias + "." + _dialect.QuoteIdentifier(property.ColumnName);

    private string AddParameter(object value, BoundProperty? property)
    {
        var name = _dialect.ParameterName(_parameters.Count);
        _parameters.Add(new QueryParameter(name, value, property));
        return name;
    }

    private string NextAlias() => _dialect.QuoteIdentifier(AliasPrefix + (_aliasCount++).ToString(CultureInfo.InvariantCulture));

    private readonly record struct Ordering(BoundProperty Property, bool Descending);

    /// <summary>One SELECT level: a table or derived table, and the clauses applied to it so far.</summary>
    private sealed class SelectState(string source, string alias)
    {
        public string Source { get; } = source;

        public string Alias { get; } = alias;

        public List<string> Predicates { get; } = [];

        public List<Ordering> Ordering { get; } = [];

        public string? Offset { get; set; }

        public string? Limit { get; set; }

        public bool HasPaging => Offset is not null || Limit is not null;
    }
}
