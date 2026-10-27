using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates a predicate lambda into template construction code, and records where in the expression tree the
/// compiler will put each value. Only the milestone 1 shapes are accepted; anything else is a diagnostic.
/// </summary>
internal sealed class PredicateTranslator
{
    public const string BodyVariable = "body";

    private const string Expressions = "global::System.Linq.Expressions";
    private const string Templates = "global::KetPsi.EntityFrameworkCore.Infrastructure.Templates";
    private const string DbFunctions = "Microsoft.EntityFrameworkCore.DbFunctions";
    private const string ContainsMethod = "Contains";
    private const string EqualsMethod = "Equals";
    private const string HasValueProperty = "HasValue";

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _valuePaths = [];

    private PredicateTranslator(SemanticModel model, IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        _model = model;
        _parameter = parameter;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Translates <paramref name="lambda"/>, returning either the predicate or why it cannot be translated.</summary>
    public static (PredicateModel? Predicate, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
    {
        var parameterSyntax = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
        };

        if (parameterSyntax is null
            || model.GetDeclaredSymbol(parameterSyntax, cancellationToken) is not { } parameter
            || lambda.Body is not ExpressionSyntax body)
        {
            return (null, Diagnostic(DiagnosticDescriptors.ExpressionNotSupported, lambda));
        }

        var translator = new PredicateTranslator(model, parameter, cancellationToken);
        try
        {
            var code = translator.TranslatePredicate(body, BodyVariable);
            return (new PredicateModel(code, translator._valuePaths.ToEquatableArray()), null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    private string TranslatePredicate(ExpressionSyntax node, string path)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (node)
        {
            case ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax when StripParentheses(node) != node:
                return TranslatePredicate(StripParentheses(node), path);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                return $"new {Templates}.NotTemplate({TranslatePredicate(unary.Operand, Operand(path))})";

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
                return $"new {Templates}.AndTemplate({TranslatePredicate(binary.Left, Left(path))}, {TranslatePredicate(binary.Right, Right(path))})";

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression):
                return $"new {Templates}.OrTemplate({TranslatePredicate(binary.Left, Left(path))}, {TranslatePredicate(binary.Right, Right(path))})";

            case BinaryExpressionSyntax binary when ComparisonOf(binary) is { } comparison:
                return TranslateComparison(binary, comparison, path);

            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: HasValueProperty } hasValue
                when EntityProperty(hasValue.Expression) is { } nullableProperty:
                return NullCheck(nullableProperty, isNull: false);

            case MemberAccessExpressionSyntax member when EntityProperty(member) is { } property:
                return property.Type.SpecialType == SpecialType.System_Boolean
                    ? $"new {Templates}.BooleanPropertyTemplate(\"{property.Name}\")"
                    : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);

            case InvocationExpressionSyntax invocation:
                return TranslateInvocation(invocation, path);

            default:
                throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
        }
    }

    private string TranslateComparison(BinaryExpressionSyntax binary, string comparison, string path)
    {
        var leftProperty = EntityProperty(binary.Left);
        var rightProperty = EntityProperty(binary.Right);
        if (leftProperty is not null && rightProperty is not null)
        {
            throw Fail(DiagnosticDescriptors.MemberComparisonNotSupported, binary);
        }

        var (property, value, valuePath) = (leftProperty, rightProperty) switch
        {
            ({ } left, _) => (left, binary.Right, Right(path)),
            (_, { } right) => (right, binary.Left, Left(path)),
            _ => throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary),
        };

        // Templates keep the property on the left.
        if (rightProperty is not null)
        {
            comparison = Flip(comparison);
        }

        if (IsNullLiteral(value))
        {
            return comparison switch
            {
                "Equal" => NullCheck(property, isNull: true),
                "NotEqual" => NullCheck(property, isNull: false),
                _ => throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary),
            };
        }

        var slot = AddValue(value, valuePath);
        return $"new {Templates}.ComparisonTemplate(\"{property.Name}\", {Templates}.ComparisonOperator.{comparison}, {slot})";
    }

    private string TranslateInvocation(InvocationExpressionSyntax invocation, string path)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method)
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, invocation);
        }

        var definition = method.ReducedFrom ?? method;
        if (definition.Parameters.Length > 0 && definition.Parameters[0].Type.ToDisplayString() == DbFunctions)
        {
            throw Fail(DiagnosticDescriptors.DbFunctionsNotSupported, invocation);
        }

        var receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        var arguments = invocation.ArgumentList.Arguments;

        if (method.Name == ContainsMethod && arguments.Count > 0 && EntityProperty(arguments[arguments.Count - 1].Expression) is { } item)
        {
            return TranslateContains(invocation, method, receiver, item, path);
        }

        if (method.Name == EqualsMethod && !method.IsStatic && arguments.Count == 1 && receiver is not null
            && EntityProperty(receiver) is { } compared)
        {
            var value = arguments[0].Expression;
            if (IsNullLiteral(value))
            {
                return NullCheck(compared, isNull: true);
            }

            var slot = AddValue(value, Argument(path, 0));
            return $"new {Templates}.ComparisonTemplate(\"{compared.Name}\", {Templates}.ComparisonOperator.Equal, {slot})";
        }

        if (IsNavigationCollection(receiver ?? (arguments.Count > 0 ? arguments[0].Expression : null)))
        {
            throw Fail(DiagnosticDescriptors.CollectionNotSupported, invocation);
        }

        throw Fail(
            ReferencesParameter(invocation) ? DiagnosticDescriptors.MethodNotSupported : DiagnosticDescriptors.ExpressionNotSupported,
            invocation);
    }

    // list.Contains(x.Prop): the list is the instance for List<T>.Contains, and the first argument for
    // Enumerable.Contains and MemoryExtensions.Contains, whether called as an extension or not.
    private string TranslateContains(
        InvocationExpressionSyntax invocation, IMethodSymbol method, ExpressionSyntax? receiver, IPropertySymbol item, string path)
    {
        var arguments = invocation.ArgumentList.Arguments;
        ExpressionSyntax list;
        string listPath;
        if (method.ReducedFrom is not null)
        {
            list = receiver!;
            listPath = Argument(path, 0);
        }
        else if (method.IsStatic)
        {
            list = arguments[0].Expression;
            listPath = Argument(path, 0);
        }
        else
        {
            list = receiver ?? throw Fail(DiagnosticDescriptors.ExpressionNotSupported, invocation);
            listPath = $"(({Expressions}.MethodCallExpression){path}).Object!";
        }

        if (ReferencesParameter(list))
        {
            throw Fail(
                IsNavigationCollection(list) ? DiagnosticDescriptors.CollectionNotSupported : DiagnosticDescriptors.ExpressionNotSupported,
                invocation);
        }

        var slot = AddValue(list, listPath);
        return $"new {Templates}.InListTemplate(\"{item.Name}\", {slot})";
    }

    /// <summary>
    /// The entity property <paramref name="node"/> reads directly (<c>x.Prop</c>), or null when it does not involve
    /// the lambda parameter. Other uses of the parameter are diagnostics.
    /// </summary>
    private IPropertySymbol? EntityProperty(ExpressionSyntax node)
    {
        node = StripParentheses(node);
        if (!ReferencesParameter(node))
        {
            return null;
        }

        if (node is MemberAccessExpressionSyntax member)
        {
            if (IsParameter(member.Expression))
            {
                if (_model.GetSymbolInfo(member, _cancellationToken).Symbol is not IPropertySymbol property)
                {
                    throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
                }

                return EntityShapeBuilder.IsReadableScalar(property.Type)
                    ? property
                    : throw Fail(DiagnosticDescriptors.NavigationNotSupported, node);
            }

            // x.Prop.Member: a navigation's property, or a member of a scalar such as string.Length.
            if (StripParentheses(member.Expression) is MemberAccessExpressionSyntax inner && IsParameter(inner.Expression)
                && _model.GetSymbolInfo(inner, _cancellationToken).Symbol is IPropertySymbol innerProperty)
            {
                throw Fail(
                    EntityShapeBuilder.IsReadableScalar(innerProperty.Type)
                        ? DiagnosticDescriptors.MethodNotSupported
                        : DiagnosticDescriptors.NavigationNotSupported,
                    node);
            }

            throw Fail(DiagnosticDescriptors.NavigationNotSupported, node);
        }

        throw Fail(
            node is InvocationExpressionSyntax ? DiagnosticDescriptors.MethodNotSupported : DiagnosticDescriptors.ExpressionNotSupported,
            node);
    }

    /// <summary>Records a value slot, after checking the value is something the runtime can read from the tree.</summary>
    private int AddValue(ExpressionSyntax value, string path)
    {
        if (!IsReadableValue(value))
        {
            throw Fail(DiagnosticDescriptors.ValueNotSupported, value);
        }

        _valuePaths.Add(path);
        return _valuePaths.Count - 1;
    }

    // A constant, a variable, this, or a field or property chain on one of those.
    private bool IsReadableValue(ExpressionSyntax value)
    {
        switch (StripParentheses(value))
        {
            case LiteralExpressionSyntax or DefaultExpressionSyntax or ThisExpressionSyntax:
                return true;
            case IdentifierNameSyntax identifier:
                return _model.GetSymbolInfo(identifier, _cancellationToken).Symbol switch
                {
                    IParameterSymbol parameter => !SymbolEqualityComparer.Default.Equals(parameter, _parameter),
                    ILocalSymbol or IFieldSymbol or IPropertySymbol => true,
                    _ => false,
                };
            case MemberAccessExpressionSyntax member:
                return _model.GetSymbolInfo(member, _cancellationToken).Symbol is IFieldSymbol or IPropertySymbol
                    && (IsTypeName(member.Expression) || IsReadableValue(member.Expression));
            case CastExpressionSyntax cast:
                return _model.GetOperation(cast, _cancellationToken) is IConversionOperation { Conversion.IsUserDefined: false }
                    && IsReadableValue(cast.Expression);
            default:
                return false;
        }
    }

    private bool IsTypeName(ExpressionSyntax node)
        => _model.GetSymbolInfo(node, _cancellationToken).Symbol is INamedTypeSymbol or INamespaceSymbol;

    private bool IsNavigationCollection(ExpressionSyntax? node)
        => node is not null
            && StripParentheses(node) is MemberAccessExpressionSyntax member
            && IsParameter(member.Expression)
            && _model.GetSymbolInfo(member, _cancellationToken).Symbol is IPropertySymbol property
            && !EntityShapeBuilder.IsReadableScalar(property.Type);

    private bool IsParameter(ExpressionSyntax node)
        => StripParentheses(node) is IdentifierNameSyntax identifier
            && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(identifier, _cancellationToken).Symbol, _parameter);

    private bool ReferencesParameter(SyntaxNode node)
        => node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(IsParameter);

    private bool IsNullLiteral(ExpressionSyntax node)
        => StripParentheses(node) is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression)
            || (StripParentheses(node) is LiteralExpressionSyntax or DefaultExpressionSyntax
                && _model.GetConstantValue(node, _cancellationToken) is { HasValue: true, Value: null });

    private static string? ComparisonOf(BinaryExpressionSyntax binary) => binary.Kind() switch
    {
        SyntaxKind.EqualsExpression => "Equal",
        SyntaxKind.NotEqualsExpression => "NotEqual",
        SyntaxKind.LessThanExpression => "LessThan",
        SyntaxKind.LessThanOrEqualExpression => "LessThanOrEqual",
        SyntaxKind.GreaterThanExpression => "GreaterThan",
        SyntaxKind.GreaterThanOrEqualExpression => "GreaterThanOrEqual",
        _ => null,
    };

    private static string Flip(string comparison) => comparison switch
    {
        "LessThan" => "GreaterThan",
        "LessThanOrEqual" => "GreaterThanOrEqual",
        "GreaterThan" => "LessThan",
        "GreaterThanOrEqual" => "LessThanOrEqual",
        _ => comparison,
    };

    private static string NullCheck(IPropertySymbol property, bool isNull)
        => $"new {Templates}.NullCheckTemplate(\"{property.Name}\", isNull: {(isNull ? "true" : "false")})";

    // Parentheses and the null-forgiving operator have no node in the expression tree.
    private static ExpressionSyntax StripParentheses(ExpressionSyntax node)
    {
        while (true)
        {
            switch (node)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    node = suppressed.Operand;
                    break;
                default:
                    return node;
            }
        }
    }

    private static string Left(string path) => $"(({Expressions}.BinaryExpression){path}).Left";

    private static string Right(string path) => $"(({Expressions}.BinaryExpression){path}).Right";

    private static string Operand(string path) => $"(({Expressions}.UnaryExpression){path}).Operand";

    private static string Argument(string path, int index) => $"(({Expressions}.MethodCallExpression){path}).Arguments[{index}]";

    private static DiagnosticInfo Diagnostic(DiagnosticDescriptor descriptor, SyntaxNode node)
        => DiagnosticInfo.Create(descriptor, LocationInfo.From(node.GetLocation()), node.ToString());

    private static TranslationException Fail(DiagnosticDescriptor descriptor, SyntaxNode node) => new(Diagnostic(descriptor, node));

    private sealed class TranslationException(DiagnosticInfo diagnostic) : Exception(diagnostic.Descriptor.Id)
    {
        public DiagnosticInfo Diagnostic { get; } = diagnostic;
    }
}
