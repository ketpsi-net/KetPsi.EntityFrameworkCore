using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// Whether a collection navigation has a related row: <c>x.Posts.Any()</c> or <c>x.Posts.Any(p =&gt; p.Rating &gt; 3)</c>,
/// written as <c>EXISTS</c>. Its negation is <c>!x.Posts.Any()</c>; <c>x.Posts.All(p =&gt; c)</c> is the negation of
/// <c>x.Posts.Any(p =&gt; !c)</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ExistsTemplate : PredicateTemplate
{
    /// <summary>Creates the template.</summary>
    public ExistsTemplate(CollectionTemplate collection)
        : base(collection?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(collection);

        Collection = collection;
    }

    /// <summary>The related rows.</summary>
    public CollectionTemplate Collection { get; }
}
