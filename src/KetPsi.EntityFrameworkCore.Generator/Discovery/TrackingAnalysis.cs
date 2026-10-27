using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Finds terminals that visibly run a tracking query: the call chain in the same expression starts at a DbSet and
/// never calls <c>AsNoTracking</c>. Queries composed across statements are checked when they run instead.
/// </summary>
internal static class TrackingAnalysis
{
    private const string AsNoTracking = "AsNoTracking";
    private const string SetMethod = "Set";

    // Operators a query passes through on its way to the terminal. Anything else ends the analysis: unsupported
    // operators already have their own diagnostic.
    private static readonly HashSet<string> ComposingOperators = new(StringComparer.Ordinal)
    {
        "Where", "WhereIf", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Skip", "Take", "IgnoreQueryFilters",
    };

    public static bool ReturnsEntities(OperatorKind kind) => kind is OperatorKind.ToList or OperatorKind.ToArray
        or OperatorKind.First or OperatorKind.FirstOrDefault or OperatorKind.Single or OperatorKind.SingleOrDefault;

    public static bool RunsTrackedQuery(InvocationExpressionSyntax terminal, SemanticModel model, CancellationToken cancellationToken)
    {
        if (terminal.Expression is not MemberAccessExpressionSyntax terminalAccess)
        {
            return false;
        }

        var expression = terminalAccess.Expression;
        while (true)
        {
            expression = PredicateTranslator.StripParentheses(expression);
            if (expression is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
            {
                return IsDbSet(model.GetTypeInfo(expression, cancellationToken).Type);
            }

            var name = access.Name.Identifier.ValueText;
            if (name == AsNoTracking)
            {
                return false;
            }

            if (name == SetMethod)
            {
                return IsDbSet(model.GetTypeInfo(expression, cancellationToken).Type);
            }

            if (!ComposingOperators.Contains(name))
            {
                return false;
            }

            expression = access.Expression;
        }
    }

    private static bool IsDbSet(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == KnownTypes.DbSetDefinition)
            {
                return true;
            }
        }

        return false;
    }
}
