using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>A <see cref="PredicateTemplate"/> with its property names bound to columns.</summary>
internal abstract record BoundPredicate;

internal sealed record BoundComparison(BoundProperty Property, ComparisonOperator Comparison, int ValueIndex) : BoundPredicate;

internal sealed record BoundBooleanProperty(BoundProperty Property) : BoundPredicate;

internal sealed record BoundNullCheck(BoundProperty Property, bool IsNull) : BoundPredicate;

internal sealed record BoundInList(BoundProperty Property, int ValueIndex) : BoundPredicate;

internal sealed record BoundNot(BoundPredicate Operand) : BoundPredicate;

internal sealed record BoundAnd(BoundPredicate Left, BoundPredicate Right) : BoundPredicate;

internal sealed record BoundOr(BoundPredicate Left, BoundPredicate Right) : BoundPredicate;
