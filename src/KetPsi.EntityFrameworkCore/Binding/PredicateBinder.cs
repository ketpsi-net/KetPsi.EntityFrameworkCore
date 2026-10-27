using System.Diagnostics;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

internal static class PredicateBinder
{
    public static BoundPredicate Bind(PredicateTemplate template, BoundEntity entity) => template switch
    {
        ComparisonTemplate comparison => new BoundComparison(
            entity.GetProperty(comparison.PropertyName), comparison.Comparison, comparison.ValueIndex),
        BooleanPropertyTemplate boolean => new BoundBooleanProperty(GetBooleanProperty(entity, boolean.PropertyName)),
        NullCheckTemplate nullCheck => new BoundNullCheck(entity.GetProperty(nullCheck.PropertyName), nullCheck.IsNull),
        InListTemplate inList => new BoundInList(entity.GetProperty(inList.PropertyName), inList.ValueIndex),
        NotTemplate not => new BoundNot(Bind(not.Operand, entity)),
        AndTemplate and => new BoundAnd(Bind(and.Left, entity), Bind(and.Right, entity)),
        OrTemplate or => new BoundOr(Bind(or.Left, entity), Bind(or.Right, entity)),
        _ => throw new UnreachableException(template.GetType().ToString()),
    };

    private static BoundProperty GetBooleanProperty(BoundEntity entity, string propertyName)
    {
        var property = entity.GetProperty(propertyName);
        if (property.ClrType != typeof(bool))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, ExceptionMessages.PropertyNotBoolean, entity.ClrType, propertyName, property.ClrType));
        }

        return property;
    }
}
