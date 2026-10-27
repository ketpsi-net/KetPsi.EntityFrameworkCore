using System.Collections.Immutable;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    private const string UseQueryTrackingBehavior = "UseQueryTrackingBehavior";
    private const string QueryTrackingBehaviorProperty = "QueryTrackingBehavior";
    private const string RuntimeReaderRegistry = "KetPsi.EntityFrameworkCore.Infrastructure.EntityReaders";
    private const string SqlServerDialectType = "KetPsi.EntityFrameworkCore.SqlServer.SqlServerDialect";

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

        // A context may default to no-tracking; then only the runtime can tell whether a query tracks.
        var trackingBehaviorConfigured = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => IsTrackingBehaviorReference(node), static (_, _) => true)
            .Collect()
            .Select(static (references, _) => !references.IsEmpty);

        var references = context.CompilationProvider.Select(static (compilation, _) => GetReferences(compilation));

        context.RegisterSourceOutput(
            entitySites.Combine(interceptorsEnabled).Combine(trackingBehaviorConfigured),
            static (spc, pair) => Report(spc, pair.Left.Left, pair.Left.Right, pair.Right));
        context.RegisterSourceOutput(entitySites.Combine(references), static (spc, pair) => EmitReaders(spc, pair.Left, pair.Right));
        context.RegisterSourceOutput(
            entitySites.Combine(interceptorsEnabled).Combine(references),
            static (spc, pair) => EmitInterceptors(spc, pair.Left.Left, pair.Left.Right, pair.Right));
    }

    /// <summary>The sites that get an interceptor, in source order so the output is stable.</summary>
    internal static IReadOnlyList<CallSite> InterceptedSites(EquatableArray<CallSite> sites)
        => sites
            .Where(InterceptorEmitter.CanIntercept)
            .OrderBy(static s => s.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static s => s.Location.Span.Start)
            .ToList();

    // Without interceptors enabled the compiler would reject the generated code, and KP0002 already explains why.
    private static void EmitInterceptors(
        SourceProductionContext context, EquatableArray<CallSite> sites, bool interceptorsEnabled, GenerationReferences references)
    {
        var intercepted = InterceptedSites(sites);
        if (!interceptorsEnabled || !references.HasRuntime || intercepted.Count == 0)
        {
            return;
        }

        context.AddSource(InterceptorEmitter.HintName, InterceptorEmitter.Emit(intercepted));
    }

    /// <summary>The entities that get a reader: element types of supported operators, once each.</summary>
    internal static IReadOnlyList<EntityShape> ReadableEntities(EquatableArray<CallSite> sites)
        => sites
            .Where(static s => s.Operator is not null && s.Entity is not null)
            .Select(static s => s.Entity!)
            .GroupBy(static e => e.TypeName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static e => e.TypeName, StringComparer.Ordinal)
            .ToList();

    internal static GenerationReferences GetReferences(Compilation compilation)
        => new(
            HasRuntime: compilation.GetTypeByMetadataName(RuntimeReaderRegistry) is not null,
            HasSqlServer: compilation.GetTypeByMetadataName(SqlServerDialectType) is not null);

    private static void EmitReaders(SourceProductionContext context, EquatableArray<CallSite> sites, GenerationReferences references)
    {
        var entities = ReadableEntities(sites);
        foreach (var diagnostic in entities.SelectMany(static e => e.Diagnostics))
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        var readable = entities.Where(static e => e.CanRead).ToList();
        if (!references.HasRuntime || readable.Count == 0)
        {
            return;
        }

        context.AddSource(ReaderEmitter.HintName, ReaderEmitter.Emit(readable, references.HasSqlServer));
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

    internal static bool IsTrackingBehaviorReference(SyntaxNode node)
        => node is IdentifierNameSyntax { Identifier.ValueText: UseQueryTrackingBehavior or QueryTrackingBehaviorProperty };

    private static void Report(SourceProductionContext context, EquatableArray<CallSite> sites, bool interceptorsEnabled, bool trackingBehaviorConfigured)
    {
        foreach (var site in sites)
        {
            if (site.Diagnostic is { } diagnostic)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (site.RunsTrackedQuery && !trackingBehaviorConfigured)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    DiagnosticDescriptors.TrackingNotSupported, site.Location, $"'{site.Signature?.Name}' runs a tracking query").ToDiagnostic());
            }
        }

        if (!interceptorsEnabled && sites.Any(static s => s.Operator is not null))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InterceptorsNotEnabled, Location.None, GeneratedNamespace));
        }
    }
}
