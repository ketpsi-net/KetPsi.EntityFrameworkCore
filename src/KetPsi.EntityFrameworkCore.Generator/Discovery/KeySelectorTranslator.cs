using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates an ordering key selector, which reads one scalar property of the entity or of a reference navigation:
/// <c>x =&gt; x.Name</c> or <c>x =&gt; x.Blog.Name</c>. The key is the property path.
/// </summary>
internal static class KeySelectorTranslator
{
    public static (string? Property, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
    {
        var parameterSyntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        if (parameterSyntax is not null
            && model.GetDeclaredSymbol(parameterSyntax, cancellationToken) is { } parameter
            && lambda.Body is ExpressionSyntax body
            && PathOf(body, parameter, model, cancellationToken) is { } path)
        {
            return (path, null);
        }

        var node = lambda.Body as ExpressionSyntax ?? (SyntaxNode)lambda;
        return (null, DiagnosticInfo.Create(DiagnosticDescriptors.OrderKeyNotSupported, LocationInfo.From(node.GetLocation()), node.ToString()));
    }

    // x.Prop, or x.Nav.Prop through reference navigations only.
    private static string? PathOf(ExpressionSyntax body, ISymbol parameter, SemanticModel model, CancellationToken cancellationToken)
    {
        var chain = new List<IPropertySymbol>();
        var current = PredicateTranslator.StripParentheses(body);
        while (current is MemberAccessExpressionSyntax member)
        {
            if (model.GetSymbolInfo(member, cancellationToken).Symbol is not IPropertySymbol property)
            {
                return null;
            }

            chain.Insert(0, property);
            current = PredicateTranslator.StripParentheses(member.Expression);
        }

        if (chain.Count == 0
            || current is not IdentifierNameSyntax identifier
            || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter)
            || !EntityShapeBuilder.IsReadableScalar(chain[chain.Count - 1].Type))
        {
            return null;
        }

        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (EntityShapeBuilder.IsCollectionNavigation(chain[i].Type) || EntityShapeBuilder.NavigationTarget(chain[i].Type) is null)
            {
                return null;
            }
        }

        return string.Join(".", chain.Select(static p => p.Name));
    }
}
