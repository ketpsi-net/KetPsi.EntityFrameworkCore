using System.Collections.Immutable;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KetPsi.EntityFrameworkCore.Generator;

/// <summary>
/// Intercepts every query operator called on an entity query, so the query runs through KetPsi.EntityFrameworkCore
/// instead of EF Core's runtime pipeline. Anything it cannot translate is a build error.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class QueryInterceptorGenerator : IIncrementalGenerator
{
    /// <summary>The namespace of the generated interceptors, which must be listed in <c>InterceptorsNamespaces</c>.</summary>
    public const string GeneratedNamespace = "KetPsi.EntityFrameworkCore.Generated";

    internal const string InterceptorsNamespacesProperty = "build_property.InterceptorsNamespaces";
    internal const string InterceptorsPreviewNamespacesProperty = "build_property.InterceptorsPreviewNamespaces";

    private static readonly char[] NamespaceSeparators = [';', ','];

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var symbolEntities = context.CompilationProvider.Select(static (compilation, ct) => EntityDiscovery.FromSymbols(compilation, ct));
        var modelCallEntities = context.SyntaxProvider
            .CreateSyntaxProvider(EntityDiscovery.IsModelCall, EntityDiscovery.GetModelCallEntity)
            .Where(static name => name is not null)
            .Collect();
        var entities = symbolEntities
            .Combine(modelCallEntities)
            .Select(static (pair, _) => EntityDiscovery.Merge(pair.Left, pair.Right));

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(CallSiteAnalyzer.IsCandidate, CallSiteAnalyzer.Analyze)
            .Where(static site => site is not null)
            .Collect();
        var entitySites = callSites
            .Combine(entities)
            .Select(static (pair, _) => ForEntities(pair.Left, pair.Right));

        var interceptorsEnabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => InterceptorsEnabled(options.GlobalOptions));

        context.RegisterSourceOutput(entitySites.Combine(interceptorsEnabled), static (spc, pair) => Report(spc, pair.Left, pair.Right));
    }

    /// <summary>Keeps the calls on entity queries, plus internal errors, which are reported whatever the query is.</summary>
    internal static EquatableArray<CallSite> ForEntities(ImmutableArray<CallSite?> sites, EquatableArray<string> entities)
    {
        var entitySet = new HashSet<string>(entities, StringComparer.Ordinal);
        return sites
            .Where(site => site is not null
                && (site.ElementType == CallSiteAnalyzer.UnknownElementType || entitySet.Contains(site.ElementType)))
            .Select(static site => site!)
            .ToEquatableArray();
    }

    internal static bool InterceptorsEnabled(AnalyzerConfigOptions options)
        => ListsGeneratedNamespace(options, InterceptorsNamespacesProperty)
            || ListsGeneratedNamespace(options, InterceptorsPreviewNamespacesProperty);

    private static bool ListsGeneratedNamespace(AnalyzerConfigOptions options, string property)
        => options.TryGetValue(property, out var value)
            && value.Split(NamespaceSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Any(static n => string.Equals(n.Trim(), GeneratedNamespace, StringComparison.Ordinal));

    private static void Report(SourceProductionContext context, EquatableArray<CallSite> sites, bool interceptorsEnabled)
    {
        foreach (var site in sites)
        {
            if (site.Diagnostic is { } diagnostic)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        if (!interceptorsEnabled && sites.Any(static s => s.Operator is not null))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InterceptorsNotEnabled, Location.None, GeneratedNamespace));
        }
    }
}
