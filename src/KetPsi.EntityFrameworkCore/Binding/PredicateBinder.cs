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
