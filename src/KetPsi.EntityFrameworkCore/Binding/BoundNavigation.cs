namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>
/// A navigation of an entity, bound to the foreign key that relates it to its target. The target entity is bound on
/// demand with <see cref="ModelBinder.GetEntity"/>, so navigation cycles such as Blog to Post to Blog bind lazily.
/// </summary>
/// <param name="Name">The navigation name.</param>
/// <param name="ClrType">The navigation's CLR type, such as <c>Blog</c> or <c>List&lt;Post&gt;</c>.</param>
/// <param name="TargetClrType">The CLR type of the related entity.</param>
/// <param name="IsCollection">Whether the navigation holds many related entities.</param>
/// <param name="IsOnDependent">
/// Whether this entity holds the foreign key, as <c>Post.Blog</c> does. A collection navigation, such as
/// <c>Blog.Posts</c>, is always on the principal.
/// </param>
/// <param name="IsRequired">
/// For a reference, whether every entity has a related one, which joins the target with <c>INNER JOIN</c> instead of
/// <c>LEFT JOIN</c>, as EF Core does. Always <see langword="false"/> for a collection.
/// </param>
/// <param name="InverseName">The navigation on the target that points back, or <see langword="null"/>.</param>
/// <param name="SourceKey">The properties of this entity that join to <paramref name="TargetKey"/>, in matching order.</param>
/// <param name="TargetKey">The names of the target's properties that join to <paramref name="SourceKey"/>.</param>
internal sealed record BoundNavigation(
    string Name,
    Type ClrType,
    Type TargetClrType,
    bool IsCollection,
    bool IsOnDependent,
    bool IsRequired,
    string? InverseName,
    IReadOnlyList<BoundProperty> SourceKey,
    IReadOnlyList<string> TargetKey);
