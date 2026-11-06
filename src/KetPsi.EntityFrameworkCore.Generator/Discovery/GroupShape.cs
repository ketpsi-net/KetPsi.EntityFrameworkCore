using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// A translated <c>GroupBy</c>: the key parts and the element, which the operators on its groups read. It lives only
/// while one call site is analyzed; <see cref="Model"/> is what the interceptor needs.
/// </summary>
internal sealed class GroupShape(
    INamedTypeSymbol entity,
    IReadOnlyList<KeyPart> keyParts,
    GroupKeyKind keyKind,
    GroupElement element,
    GroupByModel model)
{
    /// <summary>The grouped entity.</summary>
    public INamedTypeSymbol Entity { get; } = entity;

    /// <summary>The key's parts, in the order of the key paths.</summary>
    public IReadOnlyList<KeyPart> KeyParts { get; } = keyParts;

    public GroupKeyKind KeyKind { get; } = keyKind;

    public GroupElement Element { get; } = element;

    public GroupByModel Model { get; } = model;

    /// <summary>For a result selector, the projection it stands for, which the operators after it work on.</summary>
    public ProjectionModel? ResultProjection { get; private init; }

    /// <summary>Returns the shape with the result selector's <c>Select</c>.</summary>
    public GroupShape WithResult(GroupCallModel result, ProjectionModel projection)
        => new(Entity, KeyParts, KeyKind, Element, Model with { Result = result }) { ResultProjection = projection };
}

internal enum GroupKeyKind
{
    /// <summary>One property: <c>g.Key</c> is the value.</summary>
    Single,

    /// <summary>An anonymous type of properties: <c>g.Key.Member</c> is a part.</summary>
    Anonymous,
}

/// <summary>A part of the key: the property it reads from the grouped entity, through reference navigations.</summary>
/// <param name="MemberName">The member of a composite key, or <see langword="null"/> for a single key.</param>
/// <param name="Path">The properties read from the entity, the last one a column.</param>
/// <param name="Owner">The entity that declares the column.</param>
internal sealed record KeyPart(string? MemberName, IReadOnlyList<IPropertySymbol> Path, INamedTypeSymbol Owner);

/// <summary>
/// What the group's elements are: the entity itself, one of its properties, or an anonymous type of its properties.
/// Aggregates read a property of the element.
/// </summary>
/// <param name="IsEntity">Whether the element is the grouped entity, without an element selector.</param>
/// <param name="Path">For an element selector that reads one property, its path.</param>
/// <param name="Members">For an element selector that builds an anonymous type of properties, each member's path.</param>
internal sealed record GroupElement(
    bool IsEntity,
    IReadOnlyList<IPropertySymbol>? Path = null,
    IReadOnlyDictionary<string, IReadOnlyList<IPropertySymbol>>? Members = null);
