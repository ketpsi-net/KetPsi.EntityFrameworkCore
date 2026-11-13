using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary><c>test ? whenTrue : whenFalse</c>: <c>CASE WHEN test THEN whenTrue ELSE whenFalse END</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ConditionalValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public ConditionalValueTemplate(PredicateTemplate test, ValueTemplate whenTrue, ValueTemplate whenFalse)
        : base(Math.Max(test?.ValueCount ?? 0, MaxValueCount(whenTrue, whenFalse)))
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(whenTrue);
        ArgumentNullException.ThrowIfNull(whenFalse);

        Test = test;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    /// <summary>The condition.</summary>
    public PredicateTemplate Test { get; }

    /// <summary>The value when <see cref="Test"/> is true.</summary>
    public ValueTemplate WhenTrue { get; }

    /// <summary>The value when <see cref="Test"/> is false.</summary>
    public ValueTemplate WhenFalse { get; }
}
