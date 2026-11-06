using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Finds decimal operators in a lambda that the compiler turns into an expression tree. The compiler builds them
/// without the operator method, so <c>System.Linq.Expressions</c> looks it up by reflection when the tree is built,
/// in the caller's code. Under Native AOT that lookup fails unless decimal's methods are kept for reflection, which
/// the interceptor then declares with <c>DynamicDependency</c>.
/// </summary>
internal static class DecimalOperators
{
    public static bool AreUsedIn(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
        => lambda.Body.DescendantNodesAndSelf().Any(node =>
            node is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
            && model.GetSymbolInfo(node, cancellationToken).Symbol is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_Decimal });
}
