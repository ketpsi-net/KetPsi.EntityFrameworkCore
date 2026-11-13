using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// The number of related rows of a collection navigation: <c>x.Posts.Count()</c>, <c>x.Posts.Count</c>,
/// <c>x.Posts.Count(p =&gt; p.Rating &gt; 3)</c> or <c>LongCount</c>. Zero when there are none, never null.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CountValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="collection">The related rows.</param>
    /// <param name="isLong"><see langword="true"/> for <c>LongCount</c>, a <see cref="long"/>.</param>
    public CountValueTemplate(CollectionTemplate collection, bool isLong = false)
        : base(collection?.ValueCount ?? 0)
    {
        ArgumentNullException.ThrowIfNull(collection);

        Collection = collection;
        IsLong = isLong;
    }

    /// <summary>The related rows.</summary>
    public CollectionTemplate Collection { get; }

    /// <summary><see langword="true"/> for <c>LongCount</c>, a <see cref="long"/>.</summary>
    public bool IsLong { get; }
}
