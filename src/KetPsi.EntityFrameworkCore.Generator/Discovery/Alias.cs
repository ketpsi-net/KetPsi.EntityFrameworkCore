using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// What a lambda parameter, or a member of one, stands for in the query: an entity at a path, or a column. A query's
/// own parameter is the queried entity at <c>""</c>; a joined one is at its join path, such as <c>"$0"</c>; and a
/// projection that only renames them, such as the pair <c>new { p, b }</c> of a query-syntax join, has an alias for
/// each member, which the operators after it read through.
/// </summary>
/// <param name="MemberPath">The member chain from the parameter, such as <c>"b"</c>; <c>""</c> for the parameter itself.</param>
/// <param name="Path">The query path it stands for: <c>""</c>, <c>"$0"</c> or <c>"Blog"</c> for an entity, <c>"$0.Name"</c> for a column.</param>
/// <param name="Entity">The entity, or the entity that has the column.</param>
/// <param name="Property">The column's property, or <see langword="null"/> for an entity.</param>
internal sealed record Alias(string MemberPath, string Path, INamedTypeSymbol Entity, IPropertySymbol? Property = null)
{
    public bool IsEntity => Property is null;

    /// <summary>A parameter that is the entity at <paramref name="path"/>.</summary>
    public static IReadOnlyList<Alias> At(string path, INamedTypeSymbol entity) => [new Alias("", path, entity)];

    /// <summary>
    /// Resolves the member chain <paramref name="operation"/> reads from one of <paramref name="parameters"/>: the alias
    /// it starts with, and the entity properties read after it. <see langword="null"/> when it reads none of them, or a
    /// member that stands for nothing, such as a computed one.
    /// </summary>
    public static AliasedPath? Resolve(IOperation operation, IReadOnlyDictionary<IParameterSymbol, IReadOnlyList<Alias>> parameters)
    {
        var chain = new List<IPropertySymbol>();
        while (operation is IPropertyReferenceOperation { Instance: { } instance, Property: var property })
        {
            chain.Insert(0, property);
            operation = instance;
        }

        if (operation is not IParameterReferenceOperation { Parameter: var parameter } || !parameters.TryGetValue(parameter, out var aliases))
        {
            return null;
        }

        // The longest member path that is an alias: an entity's properties follow it, a column ends the chain.
        for (var count = chain.Count; count >= 0; count--)
        {
            var member = string.Join(".", chain.Take(count).Select(static p => p.Name));
            if (aliases.FirstOrDefault(a => a.MemberPath == member) is not { } alias)
            {
                continue;
            }

            if (alias.IsEntity)
            {
                return new AliasedPath(alias.Path, alias.Entity, [.. chain.Skip(count)], Column: null);
            }

            return count == chain.Count ? new AliasedPath(alias.Path, alias.Entity, [], alias.Property) : null;
        }

        return null;
    }

    /// <summary>A path that starts with <paramref name="prefix"/>, followed by <paramref name="names"/>.</summary>
    public static string Combine(string prefix, IEnumerable<string> names)
        => string.Join(".", new[] { prefix }.Concat(names).Where(static n => n.Length > 0));
}

/// <summary>A member chain resolved through an <see cref="Alias"/>.</summary>
/// <param name="Prefix">The query path of the alias.</param>
/// <param name="Owner">The aliased entity, or the entity that has the aliased column.</param>
/// <param name="Rest">The entity properties read after an aliased entity; empty for a column.</param>
/// <param name="Column">The aliased column's property, or <see langword="null"/> for an entity.</param>
internal readonly record struct AliasedPath(string Prefix, INamedTypeSymbol Owner, List<IPropertySymbol> Rest, IPropertySymbol? Column)
{
    /// <summary>The query path the chain reads, such as <c>"$0.Name"</c>.</summary>
    public string Path => Alias.Combine(Prefix, Rest.Select(static p => p.Name));

    /// <summary>Whether the alias is a joined entity of <c>Join</c>, whose path starts with <c>$</c>.</summary>
    public bool IsThroughJoin => Prefix.Length > 0 && Prefix[0] == '$';
}
