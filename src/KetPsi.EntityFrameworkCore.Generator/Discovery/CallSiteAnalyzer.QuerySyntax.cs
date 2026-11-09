using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

// Query syntax, which interceptors cannot replace.
internal static partial class CallSiteAnalyzer
{
    /// <summary>A query expression, such as <c>from p in db.Posts select p</c>.</summary>
    public static bool IsQueryExpression(SyntaxNode node, CancellationToken cancellationToken) => node is QueryExpressionSyntax;

    /// <summary>
    /// Query syntax has no invocation the compiler lets an interceptor replace, so a query expression over a query of an
    /// entity would run through EF Core's pipeline. It is reported, at its first <c>from</c>, when the pipeline keeps it
    /// for an entity element type.
    /// </summary>
    public static CallSite? AnalyzeQueryExpression(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var query = (QueryExpressionSyntax)context.Node;
        var model = context.SemanticModel;
        var queryableInterface = model.Compilation.GetTypeByMetadataName(KnownTypes.QueryableInterface);
        if (queryableInterface is null
            || model.GetTypeInfo(query.FromClause.Expression, cancellationToken).Type is not { } source
            || GetElementType(source, queryableInterface) is not { } element)
        {
            return null;
        }

        var location = LocationInfo.From(query.FromClause.FromKeyword.GetLocation());
        return new CallSite(
            EntityDiscovery.TypeName(element),
            location,
            Operator: null,
            IsAsync: false,
            HasPredicate: false,
            DiagnosticInfo.Create(DiagnosticDescriptors.QuerySyntaxNotSupported, location));
    }
}
