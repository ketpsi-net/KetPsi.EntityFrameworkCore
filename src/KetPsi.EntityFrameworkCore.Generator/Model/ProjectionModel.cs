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
/// <param name="HasComputedValues">
/// Whether a member is computed in C# from what is read, such as <c>x.Name + "!"</c>, as EF Core evaluates a final
/// projection on the client.
/// </param>
/// <param name="CaptureCount">
/// How many captured values the reader uses, from <c>captures</c>: the values the interceptor reads from the lambda's
/// expression tree, in source order.
/// </param>
/// <param name="GroupValues">
/// For a <c>Select</c> on groups, C# that creates each <c>GroupValue</c> the reader reads, in the order of its
/// ordinals; it reads no property paths or entities.
/// </param>
/// <param name="PredicateValues">For a <c>Select</c> on groups, C# that reads each value slot of its count predicates.</param>
/// <param name="GroupMembers">
/// For a <c>Select</c> on groups, the members that are a key part or an aggregate as they are, which a <c>Where</c> or
/// an ordering after it can read; the name is empty for a projection of one such value.
/// </param>
internal sealed record ProjectionModel(
    EquatableArray<string> PropertyPaths,
    EquatableArray<string> EntityPaths,
    string ReaderCode,
    EquatableArray<EntityShape> Entities,
    bool HasComputedValues = false,
    int CaptureCount = 0,
    EquatableArray<string> GroupValues = default,
    EquatableArray<string> PredicateValues = default,
    EquatableArray<GroupMember> GroupMembers = default)
{
    /// <summary>Whether the projection is of groups.</summary>
    public bool IsGrouped => GroupValues.Count > 0;

    /// <summary>Whether the projection reads one column and nothing else, which an aggregate can work on.</summary>
    public bool IsSingleColumn => PropertyPaths.Count + GroupValues.Count == 1 && EntityPaths.Count == 0 && !HasComputedValues;
}

/// <summary>A member of a <c>Select</c> on groups that is a key part or aggregate as it is.</summary>
/// <param name="Name">The member name, or empty for the projection itself.</param>
/// <param name="ValueCode">C# that creates its <c>GroupValue</c>.</param>
internal sealed record GroupMember(string Name, string ValueCode);
