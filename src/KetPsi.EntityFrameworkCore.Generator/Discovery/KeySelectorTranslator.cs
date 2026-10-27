using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>Translates an ordering key selector, which in milestone 1 must read one scalar property: <c>x =&gt; x.Name</c>.</summary>
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
            && PredicateTranslator.StripParentheses(body) is MemberAccessExpressionSyntax member
            && PredicateTranslator.StripParentheses(member.Expression) is IdentifierNameSyntax identifier
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter)
            && model.GetSymbolInfo(member, cancellationToken).Symbol is IPropertySymbol property
            && EntityShapeBuilder.IsReadableScalar(property.Type))
        {
            return (property.Name, null);
        }

        var node = lambda.Body as ExpressionSyntax ?? (SyntaxNode)lambda;
        return (null, DiagnosticInfo.Create(DiagnosticDescriptors.OrderKeyNotSupported, LocationInfo.From(node.GetLocation()), node.ToString()));
    }
}
