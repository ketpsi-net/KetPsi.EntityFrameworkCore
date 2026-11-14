using System.ComponentModel;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c> of a value of the related rows of a collection navigation,
/// such as <c>x.Posts.Sum(p =&gt; p.Rating)</c>. As in EF Core, <c>Sum</c> of no rows is zero, and the others are
/// null, even for a selector that is never null.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CollectionAggregateTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="collection">The related rows.</param>
    /// <param name="function">The aggregate function.</param>
    /// <param name="selector">The value of each related row; its names are property paths of the related entity.</param>
    /// <param name="valueType">
    /// The numeric type the overload works in, which decides the casts and the zero, or
    /// <see cref="AggregateValueType.Column"/> for <c>Min</c> and <c>Max</c>.
    /// </param>
    public CollectionAggregateTemplate(
        CollectionTemplate collection, AggregateFunction function, ValueTemplate selector, AggregateValueType valueType)
        : base(Math.Max(collection?.ValueCount ?? 0, selector?.ValueCount ?? 0))
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(selector);
        if (!Enum.IsDefined(function))
        {
            throw new ArgumentOutOfRangeException(nameof(function), function, message: null);
        }

        if (!Enum.IsDefined(valueType))
        {
            throw new ArgumentOutOfRangeException(nameof(valueType), valueType, message: null);
        }

        Collection = collection;
        Function = function;
        Selector = selector;
        ValueType = valueType;
    }

    /// <summary>The related rows.</summary>
    public CollectionTemplate Collection { get; }

    /// <summary>The aggregate function.</summary>
    public AggregateFunction Function { get; }

    /// <summary>The value of each related row.</summary>
    public ValueTemplate Selector { get; }

    /// <summary>The numeric type the overload works in.</summary>
    public AggregateValueType ValueType { get; }
}
