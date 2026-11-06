namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>
/// A foreign key of an entity to the primary key of another, with the navigations on either side of it. EF Core fixes
/// up these navigations between the entities a tracking or identity-resolving query returns.
/// </summary>
/// <param name="Properties">The foreign key properties of this entity, in the order of the principal's key.</param>
/// <param name="PrincipalClrType">The CLR type of the principal entity.</param>
/// <param name="DependentToPrincipal">The reference on this entity to the principal, or <see langword="null"/>.</param>
/// <param name="PrincipalToDependent">The navigation on the principal to this entity, or <see langword="null"/>.</param>
/// <param name="IsUnique">Whether the principal has at most one dependent, so its navigation is a reference.</param>
internal sealed record BoundForeignKey(
    IReadOnlyList<BoundProperty> Properties,
    Type PrincipalClrType,
    string? DependentToPrincipal,
    string? PrincipalToDependent,
    bool IsUnique);
