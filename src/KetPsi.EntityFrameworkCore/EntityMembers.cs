using System.Diagnostics.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore;

/// <summary>
/// The members EF Core keeps on entity types for trimming, as its <c>DbSet&lt;TEntity&gt;</c> and <c>Set&lt;TEntity&gt;()</c>
/// declare them. Entity type parameters that reach those APIs carry the same annotation, so callers passing concrete
/// entity types satisfy it and the trimmer keeps what EF Core needs.
/// </summary>
internal static class EntityMembers
{
    public const DynamicallyAccessedMemberTypes Required =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;
}
