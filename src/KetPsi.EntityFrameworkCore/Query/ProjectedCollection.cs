using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>A collection a projection selects, bound to the model.</summary>
/// <param name="Projection">What the generated code asked for.</param>
/// <param name="Navigation">The collection navigation, of the entity at the end of the path's references.</param>
/// <param name="KeyPaths">
/// The property paths of the parent's key that the collection's foreign key matches, such as <c>Id</c> or
/// <c>Blog.Id</c>; the projection selects them too.
/// </param>
internal sealed record ProjectedCollection(CollectionProjection Projection, BoundNavigation Navigation, IReadOnlyList<string> KeyPaths);
