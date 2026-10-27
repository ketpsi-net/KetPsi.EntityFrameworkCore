using System.Collections.Immutable;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Finds the entity types: the types a query operator must be intercepted for. Entity types are recognized by
/// how EF Core models are written, so the set is the same whether the model lives in this project or a reference.
/// </summary>
internal static class EntityDiscovery
{
    private const string EntityMethod = "Entity";
    private const string SetMethod = "Set";

    /// <summary>
    /// Entity types declared through types: <c>DbSet&lt;T&gt;</c> properties of DbContext types and
    /// <c>IEntityTypeConfiguration&lt;T&gt;</c> implementations, in this compilation and in referenced assemblies
    /// that use EF Core.
    /// </summary>
    public static EquatableArray<string> FromSymbols(Compilation compilation, CancellationToken cancellationToken)
    {
        var dbContext = compilation.GetTypeByMetadataName(KnownTypes.DbContext);
        var dbSet = compilation.GetTypeByMetadataName(KnownTypes.DbSet);
        var configuration = compilation.GetTypeByMetadataName(KnownTypes.EntityTypeConfiguration);
        if (dbContext is null || dbSet is null)
        {
            return EquatableArray<string>.Empty;
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        var walker = new Walker(dbContext, dbSet, configuration, names, cancellationToken);

        walker.Visit(compilation.Assembly.GlobalNamespace);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (UsesEntityFrameworkCore(assembly))
            {
                walker.Visit(assembly.GlobalNamespace);
            }
        }

        return names.ToEquatableArray();
    }

    /// <summary>Whether <paramref name="node"/> may be <c>modelBuilder.Entity&lt;T&gt;()</c> or <c>Set&lt;T&gt;()</c>.</summary>
    public static bool IsModelCall(SyntaxNode node, CancellationToken cancellationToken)
        => node is InvocationExpressionSyntax invocation && GetGenericName(invocation) is
        {
            Identifier.ValueText: EntityMethod or SetMethod,
            TypeArgumentList.Arguments.Count: 1,
        };

    /// <summary>The entity type of a <c>ModelBuilder.Entity&lt;T&gt;</c> or <c>DbContext.Set&lt;T&gt;</c> call, or <see langword="null"/>.</summary>
    public static string? GetModelCallEntity(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, cancellationToken).Symbol is not IMethodSymbol method
            || method.TypeArguments.Length != 1
            || method.TypeArguments[0] is not INamedTypeSymbol entityType)
        {
            return null;
        }

        var owner = method.ContainingType.OriginalDefinition.ToDisplayString();
        var isModelCall = (method.Name == EntityMethod && owner == KnownTypes.ModelBuilder)
            || (method.Name == SetMethod && owner == KnownTypes.DbContext);
        return isModelCall ? TypeName(entityType) : null;
    }

    /// <summary>Combines the entity types found from symbols and from model calls, sorted and without duplicates.</summary>
    public static EquatableArray<string> Merge(EquatableArray<string> fromSymbols, ImmutableArray<string?> fromModelCalls)
    {
        var names = new SortedSet<string>(fromSymbols, StringComparer.Ordinal);
        foreach (var name in fromModelCalls)
        {
            if (name is not null)
            {
                names.Add(name);
            }
        }

        return names.ToEquatableArray();
    }

    /// <summary>The name entity types are compared by.</summary>
    public static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static GenericNameSyntax? GetGenericName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax { Name: GenericNameSyntax name } => name,
        GenericNameSyntax name => name,
        _ => null,
    };

    // EF Core's own assemblies and KetPsi's declare no application entities.
    private static bool UsesEntityFrameworkCore(IAssemblySymbol assembly)
        => !assembly.Name.StartsWith(KnownTypes.EntityFrameworkCoreAssembly, StringComparison.Ordinal)
            && !assembly.Name.StartsWith(KnownTypes.KetPsiAssemblyPrefix, StringComparison.Ordinal)
            && assembly.Modules.Any(static m => m.ReferencedAssemblies.Any(static r => r.Name == KnownTypes.EntityFrameworkCoreAssembly));

    private sealed class Walker(
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol? configuration,
        SortedSet<string> names,
        CancellationToken cancellationToken)
    {
        public void Visit(INamespaceSymbol @namespace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var member in @namespace.GetNamespaceMembers())
            {
                Visit(member);
            }

            foreach (var type in @namespace.GetTypeMembers())
            {
                Visit(type);
            }
        }

        private void Visit(INamedTypeSymbol type)
        {
            if (DerivesFrom(type, dbContext))
            {
                AddDbSetProperties(type);
            }

            if (configuration is not null)
            {
                foreach (var implemented in type.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, configuration)
                        && implemented.TypeArguments[0] is INamedTypeSymbol entity)
                    {
                        names.Add(TypeName(entity));
                    }
                }
            }

            foreach (var nested in type.GetTypeMembers())
            {
                Visit(nested);
            }
        }

        private void AddDbSetProperties(INamedTypeSymbol contextType)
        {
            for (var type = contextType; type is not null && !SymbolEqualityComparer.Default.Equals(type, dbContext); type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is IPropertySymbol { Type: INamedTypeSymbol { IsGenericType: true } propertyType }
                        && SymbolEqualityComparer.Default.Equals(propertyType.OriginalDefinition, dbSet)
                        && propertyType.TypeArguments[0] is INamedTypeSymbol entity)
                    {
                        names.Add(TypeName(entity));
                    }
                }
            }
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
