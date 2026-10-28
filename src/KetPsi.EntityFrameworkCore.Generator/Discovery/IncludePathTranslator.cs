using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates an <c>Include</c> or <c>ThenInclude</c> lambda, which must read one navigation of its parameter:
/// <c>x =&gt; x.Posts</c>.
/// </summary>
internal static class IncludePathTranslator
{
    internal const string ReasonFiltered =
        "filtered includes are not supported yet; load the related entities with a separate query to filter them";

    internal const string ReasonMultiLevel = "a path is one navigation, such as x => x.Posts; use ThenInclude for the next level";

    internal const string ReasonNotNavigation = "it must read a navigation of the entity, such as x => x.Posts";

    internal const string ReasonStringPath = "string paths are not supported; use Include(x => x.Navigation) and ThenInclude";

    public static (IncludePath? Path, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
    {
        var parameterSyntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        var body = lambda.Body as ExpressionSyntax;
        var stripped = body is null ? null : PredicateTranslator.StripParentheses(body);
        if (parameterSyntax is not null
            && model.GetDeclaredSymbol(parameterSyntax, cancellationToken) is IParameterSymbol { Type: INamedTypeSymbol source } parameter
            && stripped is MemberAccessExpressionSyntax member)
        {
            var receiver = PredicateTranslator.StripParentheses(member.Expression);
            if (receiver is IdentifierNameSyntax identifier
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter)
                && model.GetSymbolInfo(member, cancellationToken).Symbol is IPropertySymbol property
                && EntityShapeBuilder.NavigationTarget(property.Type) is { } target)
            {
                return (new IncludePath(property.Name, source, target), null);
            }

            if (receiver is MemberAccessExpressionSyntax)
            {
                return Reject(lambda, ReasonMultiLevel);
            }
        }

        return Reject(lambda, stripped is InvocationExpressionSyntax ? ReasonFiltered : ReasonNotNavigation);
    }

    private static (IncludePath? Path, DiagnosticInfo? Diagnostic) Reject(LambdaExpressionSyntax lambda, string reason)
        => (null, DiagnosticInfo.Create(
            DiagnosticDescriptors.IncludePathNotSupported, LocationInfo.From(lambda.GetLocation()), lambda.ToString(), reason));
}

/// <summary>A translated include lambda: the navigation, the entity that declares it, and the entity it includes.</summary>
internal sealed record IncludePath(string Navigation, INamedTypeSymbol Source, INamedTypeSymbol Target);
