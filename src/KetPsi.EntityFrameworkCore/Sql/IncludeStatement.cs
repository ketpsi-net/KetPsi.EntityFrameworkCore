using KetPsi.EntityFrameworkCore.Binding;

namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>
/// A statement that reads an entity together with its included references, joined into the same rows, and the
/// collections that must be loaded next with the keys of the entities it read.
/// </summary>
/// <param name="Statement">The SQL and its parameters.</param>
/// <param name="Segments">
/// The entities each row holds, in column order: the read entity first, then each joined reference, every one
/// after the entity it is reached from.
/// </param>
/// <param name="Collections">The included collections, each loaded from the entities of one segment.</param>
internal sealed record IncludeStatement(
    SqlStatement Statement,
    IReadOnlyList<EntitySegment> Segments,
    IReadOnlyList<CollectionLoad> Collections);

/// <summary>The columns of one entity within a row.</summary>
/// <param name="Entity">The entity, whose <see cref="BoundEntity.Properties"/> are selected in order.</param>
/// <param name="Offset">The ordinal of the entity's first column.</param>
/// <param name="Parent">The segment the entity is reached from, or <see langword="null"/> for the read entity.</param>
/// <param name="Navigation">The navigation from <paramref name="Parent"/>, or <see langword="null"/> for the read entity.</param>
internal sealed record EntitySegment(BoundEntity Entity, int Offset, int? Parent, BoundNavigation? Navigation);

/// <summary>An included collection, loaded for the entities of segment <paramref name="Parent"/>.</summary>
internal sealed record CollectionLoad(int Parent, IncludeBranch Branch);
