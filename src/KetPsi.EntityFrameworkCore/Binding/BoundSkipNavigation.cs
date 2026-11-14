namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>
/// A many-to-many navigation, such as <c>Post.Tags</c>, through the join entity whose rows pair the entity with its
/// targets. Collection queries join the join entity to the target; loading it is not supported yet.
/// </summary>
/// <param name="Name">The navigation name.</param>
/// <param name="TargetClrType">The CLR type of the related entity.</param>
/// <param name="JoinClrType">
/// The CLR type of the join entity, or <see langword="null"/> when it is a shared-type entity, such as the
/// <c>Dictionary&lt;string, object&gt;</c> EF Core creates when the model maps none.
/// </param>
/// <param name="SourceKey">The properties of this entity the join entity's foreign key to it references.</param>
/// <param name="JoinSourceKey">The names of the join entity's properties that match <paramref name="SourceKey"/>.</param>
/// <param name="JoinTargetKey">The names of the join entity's properties that match <paramref name="TargetKey"/>.</param>
/// <param name="TargetKey">The names of the target's properties the join entity's foreign key to it references.</param>
internal sealed record BoundSkipNavigation(
    string Name,
    Type TargetClrType,
    Type? JoinClrType,
    IReadOnlyList<BoundProperty> SourceKey,
    IReadOnlyList<string> JoinSourceKey,
    IReadOnlyList<string> JoinTargetKey,
    IReadOnlyList<string> TargetKey);
