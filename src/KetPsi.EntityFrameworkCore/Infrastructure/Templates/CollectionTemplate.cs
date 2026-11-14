using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>
/// The related rows of a collection navigation, such as <c>x.Posts</c> or <c>x.Blog.Posts</c>, optionally filtered,
/// as in <c>x.Posts.Where(p =&gt; p.Rating &gt; 3)</c>. <see cref="ExistsTemplate"/>, <see cref="CountValueTemplate"/>
/// and <see cref="CollectionAggregateTemplate"/> query them with a correlated subquery, which applies the related
/// entity's query filters, as EF Core does.
/// </summary>
/// <remarks>
/// The names in <see cref="Predicate"/>, and in the selector of an aggregate, are property paths of the related entity,
/// such as <c>Rating</c> or <c>Author.Name</c>. A name that starts with <c>^</c> reads the row the navigation path
/// starts from, such as <c>^MinRating</c> for <c>b.MinRating</c> in <c>b.Posts.Any(p =&gt; p.Rating &gt;= b.MinRating)</c>;
/// each further <c>^</c> goes out one more collection.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CollectionTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="navigationPath">The collection navigation, or a path to one through references, such as <c>Blog.Posts</c>.</param>
    /// <param name="predicate">The condition on the related rows, or <see langword="null"/> for all of them.</param>
    public CollectionTemplate(string navigationPath, PredicateTemplate? predicate = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(navigationPath);

        NavigationPath = navigationPath;
        Predicate = predicate;
    }

    /// <summary>The collection navigation, or a path to one through references, such as <c>Blog.Posts</c>.</summary>
    public string NavigationPath { get; }

    /// <summary>The condition on the related rows, or <see langword="null"/> for all of them.</summary>
    public PredicateTemplate? Predicate { get; }

    /// <summary>The number of value slots the condition reads: one more than its highest value index.</summary>
    public int ValueCount => Predicate?.ValueCount ?? 0;
}
