using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Writes a value of a lambda that does not read the lambda's parameter, such as <c>limit + 1</c> or
/// <c>new OrderId(42)</c>, as C# the interceptor computes when the query runs. The variables the value captures are
/// read from the expression tree with <c>CapturedValues.ReadAll</c>, which lists them in source order; the writer marks
/// each with a placeholder that <see cref="ResolveCaptures"/> numbers once the whole lambda is known.
/// </summary>
internal sealed class CapturedValueWriter
{
    public const string Captures = ProjectionTranslator.Captures;

    private const string PlaceholderPrefix = "__kpvalue";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly CancellationToken _cancellationToken;

    public CapturedValueWriter(SemanticModel model, IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        _model = model;
        _parameter = parameter;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Whether a value was written that reads captured variables.</summary>
    public bool ReadsCaptures { get; private set; }

    /// <summary>The C# that computes <paramref name="value"/>, converted to <paramref name="type"/> when it is given.</summary>
    /// <exception cref="TranslationException">The value uses something the generated code cannot.</exception>
    public string Write(ExpressionSyntax value, ITypeSymbol? type = null)
    {
        var code = new Rewriter(this, value).Visit(value)!.NormalizeWhitespace().ToFullString();
        return type is null ? "(" + code + ")" : $"(({NameOf(type, value)})({code}))";
    }

    /// <summary>
    /// Replaces each placeholder in <paramref name="code"/> with the index <c>CapturedValues.ReadAll</c> gives the
    /// captured variable: its position among the variables <paramref name="lambdaBody"/> captures, in source order.
    /// </summary>
    public string ResolveCaptures(string code, SyntaxNode lambdaBody)
    {
        if (!code.Contains(PlaceholderPrefix))
        {
            return code;
        }

        var spans = new List<int>();
        CollectCaptures(lambdaBody, spans);
        for (var i = 0; i < spans.Count; i++)
        {
            code = code.Replace(Placeholder(spans[i]), i.ToString(CultureInfo.InvariantCulture));
        }

        return code;
    }

    private static string Placeholder(int spanStart) => PlaceholderPrefix + spanStart.ToString(CultureInfo.InvariantCulture) + "_";

    // The captured variables in source order, as the expression visitor of ReadAll meets them.
    private void CollectCaptures(SyntaxNode node, List<int> spans)
    {
        if (node is ExpressionSyntax expression && !IsName(expression))
        {
            if (IsConstant(expression))
            {
                return;
            }

            if (IsCaptured(expression, _model.GetSymbolInfo(expression, _cancellationToken).Symbol))
            {
                spans.Add(expression.SpanStart);
                return;
            }
        }

        foreach (var child in node.ChildNodes())
        {
            CollectCaptures(child, spans);
        }
    }

    private bool IsConstant(ExpressionSyntax expression)
        => _model.GetConstantValue(expression, _cancellationToken).HasValue;

    // Values the compiler captures in the tree: locals and parameters of the enclosing method, members of the
    // enclosing instance, and static fields and properties.
    private bool IsCaptured(ExpressionSyntax expression, ISymbol? symbol)
    {
        switch (symbol)
        {
            case ILocalSymbol:
                return true;
            case IParameterSymbol parameter:
                return !IsDeclaredInLambda(parameter);
            case IFieldSymbol { IsConst: false } or IPropertySymbol when symbol.IsStatic:
                return expression is SimpleNameSyntax or MemberAccessExpressionSyntax;
            case IFieldSymbol { IsConst: false } or IPropertySymbol:
                return expression is SimpleNameSyntax
                    || expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax };
            default:
                return false;
        }
    }

    // The lambda's own parameter, or one of a lambda inside it, is not a captured value.
    private bool IsDeclaredInLambda(IParameterSymbol parameter)
    {
        if (SymbolEqualityComparer.Default.Equals(parameter, _parameter))
        {
            return true;
        }

        var lambda = _parameter.ContainingSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        return lambda is not null
            && parameter.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(r.Span));
    }

    // A name that belongs to the node around it: the member after a dot, or an argument's name.
    private static bool IsName(ExpressionSyntax expression)
        => expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression
            || expression.Parent is MemberBindingExpressionSyntax
            || expression.Parent is NameColonSyntax;

    private string NameOf(ITypeSymbol type, SyntaxNode syntax)
        => type.IsAnonymousType || !IsAccessible(type)
            ? throw Fail(syntax)
            : type.ToDisplayString(TypeFormat);

    private bool IsAccessible(ISymbol symbol) => _model.Compilation.IsSymbolAccessibleWithin(symbol, _model.Compilation.Assembly);

    private string Constant(object? value, ITypeSymbol? type, SyntaxNode syntax)
    {
        var name = type is null ? null : NameOf(type, syntax);
        if (value is null)
        {
            return name is null ? "null" : $"(({name})null)";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum })
        {
            return $"(({name})({Number(value)}))";
        }

        return value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            bool b => b ? "true" : "false",
            int or long or ulong or uint or float or double or decimal => Number(value),
            _ => $"(({name})({Number(value)}))",
        };
    }

    private static string Number(object value) => value switch
    {
        float f when float.IsNaN(f) => "global::System.Single.NaN",
        float f when float.IsPositiveInfinity(f) => "global::System.Single.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "global::System.Single.NegativeInfinity",
        double d when double.IsNaN(d) => "global::System.Double.NaN",
        double d when double.IsPositiveInfinity(d) => "global::System.Double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "global::System.Double.NegativeInfinity",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong u => u.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u => u.ToString(CultureInfo.InvariantCulture) + "U",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static TranslationException Fail(SyntaxNode syntax)
        => new(DiagnosticInfo.Create(DiagnosticDescriptors.ValueNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString()));

    /// <summary>Rewrites the value so it compiles in the generated code with the same meaning.</summary>
    private sealed class Rewriter(CapturedValueWriter writer, ExpressionSyntax value) : CSharpSyntaxRewriter
    {
        private SemanticModel Model => writer._model;

        private CancellationToken CancellationToken => writer._cancellationToken;

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            switch (node)
            {
                // Code the generated method cannot hold, or whose meaning depends on where it is written.
                case LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or AnonymousObjectCreationExpressionSyntax
                    or InitializerExpressionSyntax or QueryExpressionSyntax or ThisExpressionSyntax or BaseExpressionSyntax
                    or AwaitExpressionSyntax or ThrowExpressionSyntax or WithExpressionSyntax or SwitchExpressionSyntax
                    or IsPatternExpressionSyntax or DeclarationExpressionSyntax or StackAllocArrayCreationExpressionSyntax
                    or RefExpressionSyntax or ConditionalAccessExpressionSyntax or CollectionExpressionSyntax:
                    throw Fail(node);
                case ExpressionSyntax expression when !IsName(expression) && Replacement(expression) is { } code:
                    return SyntaxFactory.ParseExpression(code);
                default:
                    return base.Visit(node);
            }
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (Model.GetSymbolInfo(node, CancellationToken).Symbol is not IMethodSymbol method || !writer.IsAccessible(method))
            {
                throw Fail(node);
            }

            if (method.IsExtensionMethod && node.Expression is MemberAccessExpressionSyntax extension)
            {
                // Called as a static method, which needs no using directive for the extension's namespace.
                var receiver = Visit(extension.Expression)!.ToFullString();
                var arguments = node.ArgumentList.Arguments.Select(a => Visit(a.Expression)!.ToFullString());
                var reduced = method.ReducedFrom ?? method;
                var typeArguments = extension.Name is GenericNameSyntax generic ? Visit(generic.TypeArgumentList)!.ToFullString() : "";
                return SyntaxFactory.ParseExpression(
                    $"{writer.NameOf(reduced.ContainingType, node)}.{method.Name}{typeArguments}({string.Join(", ", new[] { receiver }.Concat(arguments))})");
            }

            // An instance method of the enclosing type, or a local function, cannot be called from the generated code.
            if (!method.IsStatic && node.Expression is IdentifierNameSyntax or GenericNameSyntax || method.MethodKind == MethodKind.LocalFunction)
            {
                throw Fail(node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            => Model.GetSymbolInfo(node, CancellationToken).Symbol is IMethodSymbol constructor && writer.IsAccessible(constructor)
                ? base.VisitObjectCreationExpression(node)
                : throw Fail(node);

        public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            if (Model.GetOperation(node, CancellationToken) is not IObjectCreationOperation { Constructor: { } constructor, Type: { } type }
                || !writer.IsAccessible(constructor))
            {
                throw Fail(node);
            }

            var arguments = (ArgumentListSyntax)Visit(node.ArgumentList)!;
            return SyntaxFactory.ParseExpression($"new {writer.NameOf(type, node)}{arguments.ToFullString()}");
        }

        // The C# that replaces the expression, or null to rewrite its parts.
        private string? Replacement(ExpressionSyntax expression)
        {
            if (Model.GetConstantValue(expression, CancellationToken) is { HasValue: true } constant)
            {
                return writer.Constant(constant.Value, Model.GetTypeInfo(expression, CancellationToken).Type, expression);
            }

            var symbol = Model.GetSymbolInfo(expression, CancellationToken).Symbol;
            if (writer.IsCaptured(expression, symbol))
            {
                var type = Model.GetTypeInfo(expression, CancellationToken).Type ?? throw Fail(expression);
                writer.ReadsCaptures = true;
                return $"(({writer.NameOf(type, expression)}){Captures}[{Placeholder(expression.SpanStart)}]!)";
            }

            switch (symbol)
            {
                case IParameterSymbol:
                    // The lambda's parameter: the caller only writes values that do not read it.
                    throw Fail(value);
                case INamespaceSymbol @namespace when expression is NameSyntax or MemberAccessExpressionSyntax:
                    return "global::" + @namespace.ToDisplayString();
                case ITypeSymbol type when expression is NameSyntax or MemberAccessExpressionSyntax:
                    return writer.NameOf(type, expression);
                case IMethodSymbol { IsStatic: true } method when expression is SimpleNameSyntax name:
                    // A static method named without its type, from the same class or a using static directive.
                    return writer.IsAccessible(method)
                        ? writer.NameOf(method.ContainingType, expression) + "." + base.Visit(name)!.ToFullString()
                        : throw Fail(expression);
                case IFieldSymbol or IPropertySymbol or IMethodSymbol when !writer.IsAccessible(symbol):
                    throw Fail(expression);
                default:
                    return null;
            }
        }
    }
}
