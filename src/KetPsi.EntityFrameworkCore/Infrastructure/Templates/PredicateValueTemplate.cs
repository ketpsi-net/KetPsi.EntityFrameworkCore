using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// A condition used as a <see cref="bool"/> value, such as <c>x.Quantity &gt; 5</c> in
/// <c>(x.Quantity &gt; 5) == x.IsActive</c> or <c>OrderBy(x =&gt; x.Quantity &gt; 5)</c>: true or false, never null.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PredicateValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    public PredicateValueTemplate(PredicateTemplate predicate)
        : base(predicate?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        Predicate = predicate;
    }

    /// <summary>The condition.</summary>
    public PredicateTemplate Predicate { get; }
}
