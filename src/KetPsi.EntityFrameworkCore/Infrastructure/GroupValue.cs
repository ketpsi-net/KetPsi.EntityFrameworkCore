using System.ComponentModel;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// A value computed for each group of a <c>GroupBy</c>: a part of the key, or an aggregate over the group's rows.
/// The generator emits one for each <c>g.Key</c> member and each aggregate a grouped <c>Select</c>, <c>Where</c> or
/// <c>OrderBy</c> reads.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GroupValue
{
    private GroupValue(
        GroupValueKind kind,
        int keyIndex = 0,
        AggregateFunction function = default,
        string? propertyName = null,
        AggregateValueType valueType = AggregateValueType.Column,
        PredicateTemplate? predicate = null)
    {
        Kind = kind;
        KeyIndex = keyIndex;
        Function = function;
        PropertyName = propertyName;
        ValueType = valueType;
        Predicate = predicate;
    }

    internal GroupValueKind Kind { get; }

    /// <summary>For <see cref="GroupValueKind.Key"/>, the index of the key part in the <c>GroupBy</c>'s key paths.</summary>
    internal int KeyIndex { get; }

    internal AggregateFunction Function { get; }

    /// <summary>The entity property an aggregate or distinct count reads.</summary>
    internal string? PropertyName { get; }

    internal AggregateValueType ValueType { get; }

    /// <summary>For a count, the rows it counts: <c>g.Count(predicate)</c>; or <see langword="null"/> for every row.</summary>
    internal PredicateTemplate? Predicate { get; }

    /// <summary>A part of the key: <c>g.Key</c> for a single key, or <c>g.Key.Member</c> for a composite one.</summary>
    /// <param name="index">The index of the key part in the <c>GroupBy</c>'s key paths.</param>
    public static GroupValue Key(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return new GroupValue(GroupValueKind.Key, keyIndex: index);
    }

    /// <summary><c>g.Count()</c> or <c>g.LongCount()</c>, of every row or of the rows matching <paramref name="predicate"/>.</summary>
    /// <param name="isLong">Whether it is <c>LongCount</c>.</param>
    /// <param name="predicate">
    /// The predicate of <c>g.Count(predicate)</c> or <c>g.Where(predicate).Count()</c> over the group's entity, or
    /// <see langword="null"/>.
    /// </param>
    public static GroupValue Count(bool isLong, PredicateTemplate? predicate = null)
        => new(isLong ? GroupValueKind.LongCount : GroupValueKind.Count, predicate: predicate);

    /// <summary><c>g.Sum</c>, <c>g.Average</c>, <c>g.Min</c> or <c>g.Max</c> of an entity property.</summary>
    /// <param name="function">The aggregate.</param>
    /// <param name="propertyName">The property the selector reads, on the group's entity itself.</param>
    /// <param name="valueType">The numeric type of the <c>Sum</c> or <c>Average</c> overload; <see cref="AggregateValueType.Column"/> for <c>Min</c> and <c>Max</c>.</param>
    public static GroupValue Aggregate(AggregateFunction function, string propertyName, AggregateValueType valueType)
    {
        ValidateProperty(propertyName);
        if (!Enum.IsDefined(function))
        {
            throw new ArgumentOutOfRangeException(nameof(function), function, message: null);
        }

        return new GroupValue(GroupValueKind.Aggregate, function: function, propertyName: propertyName, valueType: valueType);
    }

    /// <summary><c>g.Select(x =&gt; x.Property).Distinct().Count()</c>, or <c>LongCount</c>.</summary>
    /// <param name="propertyName">The property the selector reads, on the group's entity itself.</param>
    /// <param name="isLong">Whether it is <c>LongCount</c>.</param>
    public static GroupValue DistinctCount(string propertyName, bool isLong)
    {
        ValidateProperty(propertyName);

        return new GroupValue(GroupValueKind.DistinctCount, propertyName: propertyName, valueType: isLong ? AggregateValueType.Int64 : AggregateValueType.Int32);
    }

    // Aggregates through navigations are correlated subqueries in EF Core, which a join would not match.
    private static void ValidateProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (propertyName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException(ExceptionMessages.GroupAggregateThroughNavigation, nameof(propertyName));
        }
    }
}

/// <summary>What a <see cref="GroupValue"/> computes.</summary>
internal enum GroupValueKind
{
    Key,
    Count,
    LongCount,
    Aggregate,
    DistinctCount,
}
