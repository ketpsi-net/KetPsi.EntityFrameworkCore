namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A <c>Select</c> lambda translated into what it reads and the C# that builds each result from a row.</summary>
/// <param name="PropertyPaths">
/// The property paths the reader reads, in the order of its ordinals: <c>"Name"</c> or <c>"Blog.Name"</c>.
/// </param>
/// <param name="EntityPaths">
/// The navigation paths of the entities the reader uses, in the order of its entities: <c>""</c> for the lambda's
/// parameter itself, or <c>"Blog"</c>.
/// </param>
/// <param name="ReaderCode">
/// A lambda over <c>reader</c>, <c>ordinals</c> and <c>entities</c> that builds one result: the <c>Select</c> body
/// with each property read from its column. It builds the same type, an anonymous one included.
/// </param>
/// <param name="Entities">The entities the projection selects, which need a reader and navigation accessors.</param>
internal sealed record ProjectionModel(
    EquatableArray<string> PropertyPaths,
    EquatableArray<string> EntityPaths,
    string ReaderCode,
    EquatableArray<EntityShape> Entities);
