using System.Diagnostics;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

internal static class PredicateBinder
{
    public static BoundPredicate Bind(PredicateTemplate template, BoundEntity entity)
        => Bind(template, entity.GetProperty, entity.ClrType);

    /// <summary>Binds a predicate whose property names <paramref name="resolve"/> maps to properties or computed values.</summary>
    /// <param name="template">The predicate.</param>
    /// <param name="resolve">Resolves a property name of the template.</param>
    /// <param name="owner">What the names belong to, for error messages.</param>
    public static BoundPredicate Bind(PredicateTemplate template, Func<string, BoundProperty> resolve, Type owner) => template switch
    {
        ComparisonTemplate comparison => new BoundComparison(
            resolve(comparison.PropertyName), comparison.Comparison, comparison.ValueIndex),
        BooleanPropertyTemplate boolean => new BoundBooleanProperty(GetBooleanProperty(resolve, owner, boolean.PropertyName)),
        NullCheckTemplate nullCheck => new BoundNullCheck(resolve(nullCheck.PropertyName), nullCheck.IsNull),
        InListTemplate inList => new BoundInList(resolve(inList.PropertyName), inList.ValueIndex),
        NotTemplate not => new BoundNot(Bind(not.Operand, resolve, owner)),
        AndTemplate and => new BoundAnd(Bind(and.Left, resolve, owner), Bind(and.Right, resolve, owner)),
        OrTemplate or => new BoundOr(Bind(or.Left, resolve, owner), Bind(or.Right, resolve, owner)),
        ValueComparisonTemplate comparison => new BoundValueComparison(
            Bind(comparison.Left, resolve, owner), comparison.Comparison, Bind(comparison.Right, resolve, owner)),
        BooleanValueTemplate boolean => new BoundBooleanValue(Bind(boolean.Value, resolve, owner)),
        StringMatchTemplate match => new BoundStringMatch(match.Match, Bind(match.Value, resolve, owner), Bind(match.Part, resolve, owner)),
        LikeTemplate like => new BoundLike(
            Bind(like.Value, resolve, owner), Bind(like.Pattern, resolve, owner), like.Escape is null ? null : Bind(like.Escape, resolve, owner)),
        NullOrEmptyTemplate empty => new BoundNullOrEmpty(Bind(empty.Value, resolve, owner), empty.OrWhiteSpace),
        ExistsTemplate exists => new BoundExists(exists.Collection),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    /// <summary>Binds a computed value whose property names <paramref name="resolve"/> maps to properties.</summary>
    public static BoundValue Bind(ValueTemplate template, Func<string, BoundProperty> resolve, Type owner) => template switch
    {
        PropertyValueTemplate property => new BoundPropertyValue(resolve(property.PropertyName)),
        SlotValueTemplate slot => new BoundSlotValue(slot.ValueIndex),
        BinaryValueTemplate binary => new BoundBinaryValue(binary.Operator, Bind(binary.Left, resolve, owner), Bind(binary.Right, resolve, owner)),
        UnaryValueTemplate unary => new BoundUnaryValue(unary.Operator, Bind(unary.Operand, resolve, owner)),
        ConvertValueTemplate convert => new BoundConvertValue(Bind(convert.Operand, resolve, owner), convert.Type),
        CoalesceValueTemplate coalesce => new BoundCoalesceValue(Bind(coalesce.Left, resolve, owner), Bind(coalesce.Right, resolve, owner)),
        ConditionalValueTemplate conditional => new BoundConditionalValue(
            Bind(conditional.Test, resolve, owner), Bind(conditional.WhenTrue, resolve, owner), Bind(conditional.WhenFalse, resolve, owner)),
        PredicateValueTemplate predicate => new BoundPredicateValue(Bind(predicate.Predicate, resolve, owner)),
        FunctionValueTemplate function => new BoundFunctionValue(function.Function, [.. function.Arguments.Select(a => Bind(a, resolve, owner))]),
        CountValueTemplate count => new BoundCountValue(count.Collection, count.IsLong),
        CollectionAggregateTemplate aggregate => new BoundCollectionAggregate(aggregate),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    private static BoundProperty GetBooleanProperty(Func<string, BoundProperty> resolve, Type owner, string propertyName)
    {
        var property = resolve(propertyName);
        if (property.ClrType != typeof(bool))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.PropertyNotBoolean, owner, propertyName, property.ClrType));
        }

        return property;
    }
}
