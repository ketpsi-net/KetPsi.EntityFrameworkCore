using KetPsi.EntityFrameworkCore.Infrastructure.Templates;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>A <see cref="ValueTemplate"/> with its property names bound to columns.</summary>
internal abstract record BoundValue;

internal sealed record BoundPropertyValue(BoundProperty Property) : BoundValue;

internal sealed record BoundSlotValue(int ValueIndex) : BoundValue;

internal sealed record BoundBinaryValue(ValueOperator Operator, BoundValue Left, BoundValue Right) : BoundValue;

internal sealed record BoundUnaryValue(UnaryValueOperator Operator, BoundValue Operand) : BoundValue;

internal sealed record BoundConvertValue(BoundValue Operand, Type Type) : BoundValue;

internal sealed record BoundCoalesceValue(BoundValue Left, BoundValue Right) : BoundValue;

internal sealed record BoundConditionalValue(BoundPredicate Test, BoundValue WhenTrue, BoundValue WhenFalse) : BoundValue;

internal sealed record BoundPredicateValue(BoundPredicate Predicate) : BoundValue;

internal sealed record BoundFunctionValue(ValueFunction Function, IReadOnlyList<BoundValue> Arguments) : BoundValue;

/// <summary>The number of related rows; rendered as a subquery by the builder, which resolves the collection.</summary>
internal sealed record BoundCountValue(CollectionTemplate Collection, bool IsLong) : BoundValue;

/// <summary>An aggregate of the related rows; rendered as a subquery by the builder, which resolves the collection.</summary>
internal sealed record BoundCollectionAggregate(CollectionAggregateTemplate Template) : BoundValue;
