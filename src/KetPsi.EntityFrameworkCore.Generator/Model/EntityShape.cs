namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>What a generated reader needs to know about an entity type.</summary>
/// <param name="TypeName">The fully qualified entity type.</param>
/// <param name="ReaderName">The generated reader class name.</param>
/// <param name="Construction">How the reader creates the entity.</param>
/// <param name="Properties">The properties the reader sets, in declaration order.</param>
/// <param name="Diagnostics">Why the entity, or some of its properties, cannot be read.</param>
/// <param name="Navigations">
/// The properties that can be navigations: references to classes and collections of them. Which ones EF Core maps
/// is decided at runtime from the model, so accessors are generated for every candidate of an included entity.
/// </param>
internal sealed record EntityShape(
    string TypeName,
    string ReaderName,
    Construction Construction,
    EquatableArray<PropertyShape> Properties,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<NavigationShape> Navigations = default)
{
    /// <summary>Whether a reader can be generated: the entity can be constructed and every property read.</summary>
    public bool CanRead => Construction != Construction.None && Diagnostics.Count == 0;
}

internal enum Construction
{
    /// <summary>No parameterless constructor, or the type is abstract.</summary>
    None,

    /// <summary><c>new T()</c>.</summary>
    New,

    /// <summary>A parameterless constructor the generated code cannot call directly, called through <c>UnsafeAccessor</c>.</summary>
    Accessor,
}

/// <summary>One entity property the reader sets.</summary>
/// <param name="Name">The property name, which is also the EF Core property name.</param>
/// <param name="TypeName">The fully qualified property type.</param>
/// <param name="DeclaringTypeName">The fully qualified type that declares the setter, used by accessor signatures.</param>
/// <param name="Read">The data reader call that reads the value, such as <c>GetInt32</c>.</param>
/// <param name="ValueTypeName">For enums and <c>GetFieldValue</c>, the type read before converting to the property type.</param>
/// <param name="CanBeNull">Whether the reader must check for DBNull first.</param>
/// <param name="Setter">How the reader assigns the property.</param>
internal sealed record PropertyShape(
    string Name,
    string TypeName,
    string DeclaringTypeName,
    ReadMethod Read,
    string? ValueTypeName,
    bool CanBeNull,
    Setter Setter);

/// <summary>A property that can be a navigation, and how a generated accessor reads and sets it.</summary>
/// <param name="Name">The property name.</param>
/// <param name="TypeName">The fully qualified property type, such as <c>List&lt;Post&gt;</c>.</param>
/// <param name="DeclaringTypeName">The fully qualified type that declares the property's accessors.</param>
/// <param name="ElementTypeName">For a collection, the fully qualified element type; <see langword="null"/> for a reference.</param>
/// <param name="CreateTypeName">
/// For a collection, the fully qualified type to create when the property is <see langword="null"/>, chosen as EF Core
/// does; <see langword="null"/> when no collection can be created.
/// </param>
/// <param name="Getter">How the accessor reads the property.</param>
/// <param name="Setter">How the accessor assigns the property, or <see langword="null"/> when it cannot.</param>
internal sealed record NavigationShape(
    string Name,
    string TypeName,
    string DeclaringTypeName,
    string? ElementTypeName,
    string? CreateTypeName,
    Setter Getter,
    Setter? Setter)
{
    public bool IsCollection => ElementTypeName is not null;
}

internal enum ReadMethod
{
    GetBoolean,
    GetByte,
    GetInt16,
    GetInt32,
    GetInt64,
    GetFloat,
    GetDouble,
    GetDecimal,
    GetString,
    GetGuid,
    GetDateTime,

    /// <summary><c>GetFieldValue&lt;T&gt;</c> with <see cref="PropertyShape.ValueTypeName"/>.</summary>
    GetFieldValue,
}

internal enum Setter
{
    /// <summary><c>entity.P = value</c>.</summary>
    Direct,

    /// <summary>An <c>init</c> accessor, assigned in the object initializer.</summary>
    Init,

    /// <summary>A setter the generated code cannot call directly, called through <c>UnsafeAccessor</c>.</summary>
    Accessor,
}
