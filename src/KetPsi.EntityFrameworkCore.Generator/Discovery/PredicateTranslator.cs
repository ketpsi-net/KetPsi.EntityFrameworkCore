using System.Collections.Immutable;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates a predicate lambda, or a computed order key, into template construction code, and records how the
/// interceptor reads each value: from the expression tree node the compiler puts it in, or computed in C# from the
/// variables the lambda captures. Anything that cannot be translated is a diagnostic.
/// </summary>
internal sealed class PredicateTranslator
{
    public const string BodyVariable = "body";

    private const string Expressions = "global::System.Linq.Expressions";
    private const string Templates = "global::KetPsi.EntityFrameworkCore.Infrastructure.Templates";
    private const string CapturedValues = "global::KetPsi.EntityFrameworkCore.Infrastructure.CapturedValues";
    private const string ValueProperty = "Value";
    private const string GetValueOrDefaultMethod = "GetValueOrDefault";
    private const string LikeMethod = "Like";
    private const string DbFunctionsExtensions = "Microsoft.EntityFrameworkCore.DbFunctionsExtensions";
    private const string DbFunctions = "Microsoft.EntityFrameworkCore.DbFunctions";
    private const string ContainsMethod = "Contains";
    private const string EqualsMethod = "Equals";
    private const string HasValueProperty = "HasValue";

    private readonly SemanticModel _model;
    private readonly IParameterSymbol _parameter;
    private readonly Func<ExpressionSyntax, string>? _readValue;
    private readonly Func<ExpressionSyntax, PredicateOperand?>? _operands;
    private readonly bool _allowNavigations;
    private readonly CancellationToken _cancellationToken;
    private readonly List<string> _valuePaths;
    private readonly CapturedValueWriter _writer;
    private bool _readsBody;

    // Inside a computed value, values are computed from the captures: the tree's conversions make paths unreliable.
    private int _valueDepth;

    private PredicateTranslator(
        SemanticModel model,
        IParameterSymbol parameter,
        Func<ExpressionSyntax, string>? readValue,
        Func<ExpressionSyntax, PredicateOperand?>? operands,
        List<string>? valueSlots,
        bool allowNavigations,
        CancellationToken cancellationToken)
    {
        _model = model;
        _parameter = parameter;
        _readValue = readValue;
        _operands = operands;
        _allowNavigations = allowNavigations;
        _valuePaths = valueSlots ?? [];
        _cancellationToken = cancellationToken;
        _writer = new CapturedValueWriter(model, parameter, cancellationToken);
    }

    /// <summary>Translates <paramref name="lambda"/>, returning either the predicate or why it cannot be translated.</summary>
    /// <param name="lambda">The predicate.</param>
    /// <param name="model">The semantic model of the lambda's tree.</param>
    /// <param name="cancellationToken">Cancels the translation.</param>
    /// <param name="readValue">
    /// Produces the C# that reads a value, for predicates whose values do not come from the expression tree, such as
    /// query filters. It throws <see cref="TranslationException"/> for values it cannot read. When omitted, values are
    /// read from the tree, or computed from the captured variables.
    /// </param>
    /// <param name="operands">
    /// For predicates over something other than an entity, such as the groups of a <c>GroupBy</c>: resolves an
    /// expression that uses the lambda's parameter to the operand it compares, or returns <see langword="null"/> when it
    /// is none. When omitted, the operands are the entity's properties.
    /// </param>
    /// <param name="valueSlots">
    /// A list shared with other predicates whose value slots are passed together; the predicate's slots are appended
    /// to it and numbered from its end. <see cref="PredicateModel.ValueCodes"/> then lists only this predicate's.
    /// </param>
    /// <param name="allowNavigations">
    /// Whether the predicate can read through reference navigations, such as <c>x.Blog.Name</c> or
    /// <c>x.Blog == null</c>; the operand names are then property paths.
    /// </param>
    public static (PredicateModel? Predicate, DiagnosticInfo? Diagnostic) Translate(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken,
        Func<ExpressionSyntax, string>? readValue = null,
        Func<ExpressionSyntax, PredicateOperand?>? operands = null,
        List<string>? valueSlots = null,
        bool allowNavigations = false)
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

        var translator = new PredicateTranslator(model, parameter, readValue, operands, valueSlots, allowNavigations, cancellationToken);
        try
        {
            return (translator.Finish(translator.TranslatePredicate(body, BodyVariable), body), null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    /// <summary>
    /// Translates an order key lambda into a value template: a value computed from the entity's properties, such as
    /// <c>x =&gt; x.Price * x.Quantity</c>.
    /// </summary>
    public static (PredicateModel? Key, DiagnosticInfo? Diagnostic) TranslateKey(
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
            return (null, Diagnostic(DiagnosticDescriptors.OrderKeyNotSupported, lambda));
        }

        var translator = new PredicateTranslator(
            model, parameter, readValue: null, operands: null, valueSlots: null, allowNavigations: true, cancellationToken);
        try
        {
            return (translator.Finish(translator.TranslateValue(body), body), null);
        }
        catch (TranslationException exception)
        {
            // A key no translation covers is reported as a key; a reason more precise than that is kept.
            return (null, exception.Diagnostic.Descriptor.Id == DiagnosticDescriptors.ExpressionNotSupported.Id
                ? Diagnostic(DiagnosticDescriptors.OrderKeyNotSupported, body)
                : exception.Diagnostic);
        }
    }

    // The model, with each value computed from the captures given the index ReadAll reads it at.
    private PredicateModel Finish(string templateCode, ExpressionSyntax body)
    {
        var first = _valuePaths.Count - _ownValues;
        var codes = _valuePaths.Skip(first).Select(code => _writer.ResolveCaptures(code, body)).ToEquatableArray();
        return new PredicateModel(templateCode, codes, _readsBody, _writer.ReadsCaptures);
    }

    // The number of slots this translation added to the shared list.
    private int _ownValues;

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

            // & and | on booleans evaluate both sides, which a predicate without side effects cannot tell apart.
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseAndExpression) && IsBoolean(binary):
                return $"new {Templates}.AndTemplate({TranslatePredicate(binary.Left, Left(path))}, {TranslatePredicate(binary.Right, Right(path))})";

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseOrExpression) && IsBoolean(binary):
                return $"new {Templates}.OrTemplate({TranslatePredicate(binary.Left, Left(path))}, {TranslatePredicate(binary.Right, Right(path))})";

            // A condition that does not read the row, such as a captured flag: true or false for every row.
            case var _ when !ReferencesParameter(node) && IsBoolean(node):
                return $"new {Templates}.BooleanValueTemplate(new {Templates}.SlotValueTemplate({AddValue(node, path)}))";

            case BinaryExpressionSyntax binary when ComparisonOf(binary) is { } comparison:
                return TranslateComparison(binary, comparison, path);

            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: HasValueProperty } hasValue
                when EntityProperty(hasValue.Expression) is { } nullableProperty:
                return NullCheck(nullableProperty, isNull: false);

            // A computed boolean: b ?? false, c ? x : y, a ^ b, or the Value of a nullable property.
            case var _ when IsComputedBoolean(node):
                return $"new {Templates}.BooleanValueTemplate({TranslateValue(node)})";

            case MemberAccessExpressionSyntax member when EntityProperty(member) is { } property:
                return property.Type.SpecialType == SpecialType.System_Boolean
                    ? $"new {Templates}.BooleanPropertyTemplate(\"{property.Name}\")"
                    : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);

            // An operand that is not a member access, such as the parameter itself or an aggregate.
            case IdentifierNameSyntax or InvocationExpressionSyntax when _operands is not null && _operands(StripParentheses(node)) is { } operand:
                return operand.Type.SpecialType == SpecialType.System_Boolean
                    ? $"new {Templates}.BooleanPropertyTemplate(\"{operand.Name}\")"
                    : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);

            case InvocationExpressionSyntax invocation:
                return TranslateInvocation(invocation, path);

            default:
                throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
        }
    }

    private string TranslateComparison(BinaryExpressionSyntax binary, string comparison, string path)
    {
        // x.Navigation == null, either way round.
        if (comparison is "Equal" or "NotEqual"
            && (IsNullLiteral(binary.Right) ? NavigationPath(binary.Left) : IsNullLiteral(binary.Left) ? NavigationPath(binary.Right) : null)
                is { } navigation)
        {
            return NullCheck(navigation, isNull: comparison == "Equal");
        }

        // string.Compare(a, b) > 0 and a.CompareTo(b) > 0 compare the strings, as EF Core translates them.
        if (StringComparison(binary.Left, binary.Right) is var (first, second))
        {
            return $"new {Templates}.ValueComparisonTemplate({TranslateValue(first)}, {Templates}.ComparisonOperator.{comparison}, {TranslateValue(second)})";
        }

        if (StringComparison(binary.Right, binary.Left) is var (third, fourth))
        {
            return $"new {Templates}.ValueComparisonTemplate({TranslateValue(third)}, {Templates}.ComparisonOperator.{Flip(comparison)}, {TranslateValue(fourth)})";
        }

        // Two values read from the row, or a computed one: compared in SQL.
        if (IsComputedComparison(binary))
        {
            return TranslateValueComparison(binary, comparison);
        }

        // Each side is translated in source order, so the value slots and the values they capture are too.
        var leftProperty = EntityProperty(binary.Left);
        PredicateOperand property;
        ExpressionSyntax value;
        int? slot = null;
        if (leftProperty is { } left)
        {
            if (EntityProperty(binary.Right) is not null)
            {
                throw Fail(DiagnosticDescriptors.MemberComparisonNotSupported, binary);
            }

            (property, value) = (left, binary.Right);
            if (!IsNullLiteral(value))
            {
                slot = AddValue(value, Right(path));
            }
        }
        else
        {
            if (!ReferencesParameter(binary.Right))
            {
                throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary);
            }

            value = binary.Left;
            if (!IsNullLiteral(value))
            {
                slot = AddValue(value, Left(path));
            }

            // Templates keep the property on the left.
            property = EntityProperty(binary.Right) ?? throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary);
            comparison = Flip(comparison);
        }

        if (slot is not { } index)
        {
            return comparison switch
            {
                "Equal" => NullCheck(property, isNull: true),
                "NotEqual" => NullCheck(property, isNull: false),
                _ => throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary),
            };
        }

        return $"new {Templates}.ComparisonTemplate(\"{property.Name}\", {Templates}.ComparisonOperator.{comparison}, {index})";
    }

    // Whether a comparison needs computed values: either side reads the row other than as one property.
    private bool IsComputedComparison(BinaryExpressionSyntax binary)
    {
        var (leftReads, rightReads) = (ReferencesParameter(binary.Left), ReferencesParameter(binary.Right));
        return leftReads && rightReads
            || leftReads && SimpleOperand(binary.Left) is null
            || rightReads && SimpleOperand(binary.Right) is null;
    }

    private string TranslateValueComparison(BinaryExpressionSyntax binary, string comparison)
    {
        if (_model.GetOperation(binary, _cancellationToken) is not IBinaryOperation operation)
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary);
        }

        // A user-defined ordering has no SQL; equality of a value stored through a converter compares the columns.
        if (operation.OperatorMethod is { } method && !IsDecimal(method.ContainingType) && comparison is not ("Equal" or "NotEqual"))
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary);
        }

        var left = TranslateValue(operation.LeftOperand);
        var right = TranslateValue(operation.RightOperand);
        return $"new {Templates}.ValueComparisonTemplate({left}, {Templates}.ComparisonOperator.{comparison}, {right})";
    }

    private string TranslateInvocation(InvocationExpressionSyntax invocation, string path)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method)
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, invocation);
        }

        var definition = method.ReducedFrom ?? method;
        var operation = _model.GetOperation(invocation, _cancellationToken) as IInvocationOperation;
        if (definition.Parameters.Length > 0 && definition.Parameters[0].Type.ToDisplayString() == DbFunctions)
        {
            // EF.Functions.Like(value, pattern) and Like(value, pattern, escape); the other functions are not translated yet.
            if (method.Name == LikeMethod && definition.ContainingType.ToDisplayString() == DbFunctionsExtensions
                && operation is { Arguments.Length: 3 or 4 } like
                && like.Arguments.Skip(1).All(static a => a.Value.Type?.SpecialType == SpecialType.System_String))
            {
                // Translated in source order, so the slots are numbered in it.
                var matched = TranslateValue(like.Arguments[1].Value);
                var pattern = TranslateValue(like.Arguments[2].Value);
                var escape = like.Arguments.Length == 4 ? ", " + TranslateValue(like.Arguments[3].Value) : "";
                return $"new {Templates}.LikeTemplate({matched}, {pattern}{escape})";
            }

            throw Fail(DiagnosticDescriptors.DbFunctionsNotSupported, invocation);
        }

        var receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        var arguments = invocation.ArgumentList.Arguments;

        // Contains, StartsWith and EndsWith of a string, with a string or a character; not with a StringComparison.
        if (operation is { Instance: { Type.SpecialType: SpecialType.System_String } text, Arguments.Length: 1 } match
            && method.Name is ContainsMethod or "StartsWith" or "EndsWith"
            && match.Arguments[0].Value.Type?.SpecialType is SpecialType.System_String or SpecialType.System_Char)
        {
            return $"new {Templates}.StringMatchTemplate({Templates}.StringMatch.{method.Name}, {TranslateValue(text)}, {TranslateValue(match.Arguments[0].Value)})";
        }

        // string.IsNullOrEmpty(x.Name) and string.IsNullOrWhiteSpace(x.Name).
        if (method is { IsStatic: true, Name: "IsNullOrEmpty" or "IsNullOrWhiteSpace", ContainingType.SpecialType: SpecialType.System_String }
            && operation is { Arguments.Length: 1 } empty)
        {
            var orWhiteSpace = method.Name == "IsNullOrWhiteSpace" ? "true" : "false";
            return $"new {Templates}.NullOrEmptyTemplate({TranslateValue(empty.Arguments[0].Value)}, orWhiteSpace: {orWhiteSpace})";
        }

        // Equals of two strings that are not one property and a value: compared like ==.
        if (method.Name == EqualsMethod && operation is { } equals && StringEquality(equals) is var (left, right)
            && (method.IsStatic || ReferencesParameter(right.Syntax) || SimpleOperand((ExpressionSyntax)left.Syntax) is null))
        {
            return $"new {Templates}.ValueComparisonTemplate({TranslateValue(left)}, {Templates}.ComparisonOperator.Equal, {TranslateValue(right)})";
        }

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

    // a.Equals(b) and string.Equals(a, b) of strings, without a StringComparison, which EF Core cannot translate.
    private static (IOperation Left, IOperation Right)? StringEquality(IInvocationOperation equals)
        => equals switch
        {
            { TargetMethod.IsStatic: false, Instance: { Type.SpecialType: SpecialType.System_String } instance, Arguments.Length: 1 }
                => (instance, equals.Arguments[0].Value),
            { TargetMethod: { IsStatic: true, ContainingType.SpecialType: SpecialType.System_String }, Arguments.Length: 2 }
                => (equals.Arguments[0].Value, equals.Arguments[1].Value),
            _ => null,
        };

    // The strings string.Compare(a, b) or a.CompareTo(b) compares, when it is compared with 0; otherwise null.
    private (IOperation Left, IOperation Right)? StringComparison(ExpressionSyntax compared, ExpressionSyntax zero)
    {
        if (_model.GetConstantValue(StripParentheses(zero), _cancellationToken) is not { HasValue: true, Value: 0 }
            || _model.GetOperation(StripParentheses(compared), _cancellationToken) is not IInvocationOperation invocation
            || !ReferencesParameter(compared))
        {
            return null;
        }

        return invocation switch
        {
            { TargetMethod: { Name: "Compare", IsStatic: true, ContainingType.SpecialType: SpecialType.System_String }, Arguments.Length: 2 }
                => (invocation.Arguments[0].Value, invocation.Arguments[1].Value),
            { TargetMethod.Name: "CompareTo", Instance: { Type.SpecialType: SpecialType.System_String } instance, Arguments.Length: 1 }
                when invocation.Arguments[0].Value.Type?.SpecialType == SpecialType.System_String
                => (instance, invocation.Arguments[0].Value),
            _ => null,
        };
    }

    // list.Contains(x.Prop): the list is the instance for List<T>.Contains, and the first argument for
    // Enumerable.Contains and MemoryExtensions.Contains, whether called as an extension or not.
    private string TranslateContains(
        InvocationExpressionSyntax invocation, IMethodSymbol method, ExpressionSyntax? receiver, PredicateOperand item, string path)
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

    /// <summary>A computed value: the C# that constructs its value template.</summary>
    private string TranslateValue(ExpressionSyntax node)
    {
        node = StripParentheses(node);
        if (!ReferencesParameter(node))
        {
            return Slot(node, convertedTo: null);
        }

        return _model.GetOperation(node, _cancellationToken) is { } operation
            ? TranslateValue(operation)
            : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
    }

    private string TranslateValue(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _valueDepth++;
        try
        {
            return TranslateValueOperation(operation);
        }
        finally
        {
            _valueDepth--;
        }
    }

    private string TranslateValueOperation(IOperation operation)
    {
        var syntax = operation.Syntax as ExpressionSyntax ?? throw Fail(DiagnosticDescriptors.ExpressionNotSupported, operation.Syntax);

        // A value that does not read the row is computed in C#, in the type C# converts it to.
        if (!ReferencesParameter(syntax))
        {
            return operation is IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false } implicitConversion
                ? Slot(StripParentheses(syntax), implicitConversion.Type)
                : Slot(StripParentheses(syntax), convertedTo: null);
        }

        switch (operation)
        {
            case IParenthesizedOperation parenthesized:
                return TranslateValue(parenthesized.Operand);

            case IConversionOperation conversion:
                return TranslateConversion(conversion);

            case IBinaryOperation binary when IsCondition(binary.OperatorKind) || IsLogical(binary):
                // A condition used as a value is true or false.
                return $"new {Templates}.PredicateValueTemplate({TranslatePredicate(syntax, path: "")})";

            case IBinaryOperation binary:
                return TranslateBinary(binary);

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not }:
                return $"new {Templates}.PredicateValueTemplate({TranslatePredicate(syntax, path: "")})";

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Plus, OperatorMethod: null } plus:
                return TranslateValue(plus.Operand);

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus or UnaryOperatorKind.BitwiseNegation } unary
                when unary.OperatorMethod is null || IsDecimal(unary.OperatorMethod.ContainingType):
                var unaryOperator = unary.OperatorKind == UnaryOperatorKind.Minus ? "Negate" : "BitwiseNot";
                return $"new {Templates}.UnaryValueTemplate({Templates}.UnaryValueOperator.{unaryOperator}, {TranslateValue(unary.Operand)})";

            case IConditionalOperation { WhenFalse: { } whenFalse } conditional:
                var test = TranslateCondition(conditional.Condition);
                return $"new {Templates}.ConditionalValueTemplate({test}, {TranslateValue(conditional.WhenTrue)}, {TranslateValue(whenFalse)})";

            case ICoalesceOperation coalesce:
                return $"new {Templates}.CoalesceValueTemplate({TranslateValue(coalesce.Value)}, {TranslateValue(coalesce.WhenNull)})";

            // x.Discount.Value reads the column, as in EF Core.
            case IPropertyReferenceOperation { Property.Name: ValueProperty, Instance: { } instance } when IsNullableValue(instance.Type):
                return TranslateValue(instance);

            // x.Discount.GetValueOrDefault(5) is x.Discount ?? 5; without an argument EF Core has no translation.
            case IInvocationOperation { TargetMethod.Name: GetValueOrDefaultMethod, Instance: { } nullable, Arguments.Length: 1 } orDefault
                when IsNullableValue(nullable.Type):
                return $"new {Templates}.CoalesceValueTemplate({TranslateValue(nullable)}, {TranslateValue(orDefault.Arguments[0].Value)})";

            case IPropertyReferenceOperation { Property.Name: "Length", Instance: { Type.SpecialType: SpecialType.System_String } text }:
                return Function("Length", text);

            // A value of a group, such as an aggregate, before the methods of its value.
            case IInvocationOperation when _operands is not null && _operands(syntax) is { } groupValue:
                return $"new {Templates}.PropertyValueTemplate(\"{groupValue.Name}\")";

            case IInvocationOperation invocation when StringFunction(invocation) is { } function:
                return function;

            // EF.Functions other than Like, which is a condition.
            case IInvocationOperation { TargetMethod: var dbFunction } when dbFunction.Name != LikeMethod
                && (dbFunction.ReducedFrom ?? dbFunction).Parameters.FirstOrDefault()?.Type.ToDisplayString() == DbFunctions:
                throw Fail(DiagnosticDescriptors.DbFunctionsNotSupported, syntax);

            // A condition used as a value, such as x.Name.StartsWith("a") == x.IsActive.
            case IInvocationOperation { Type.SpecialType: SpecialType.System_Boolean }:
                return $"new {Templates}.PredicateValueTemplate({TranslatePredicate(syntax, path: "")})";

            case IPropertyReferenceOperation or IParameterReferenceOperation or IInvocationOperation:
                // An operand: a property of the entity, through reference navigations, or a value of a group.
                var operand = EntityProperty(syntax) ?? throw Fail(DiagnosticDescriptors.ExpressionNotSupported, syntax);
                return $"new {Templates}.PropertyValueTemplate(\"{operand.Name}\")";

            default:
                throw Fail(DiagnosticDescriptors.ExpressionNotSupported, syntax);
        }
    }

    /// <summary>
    /// A string method EF Core translates: <c>ToUpper</c>, <c>ToLower</c>, <c>Trim</c>, <c>TrimStart</c> and
    /// <c>TrimEnd</c> without arguments, <c>Substring</c>, <c>Replace</c>, <c>IndexOf</c> of a string or a character,
    /// <c>ToString</c> of a string or an integer, and <c>string.Concat</c> of strings; null for anything else.
    /// </summary>
    private string? StringFunction(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var arguments = invocation.Arguments;
        if (method is { IsStatic: true, Name: "Concat", ContainingType.SpecialType: SpecialType.System_String }
            && arguments.Length is >= 2 and <= 4 && arguments.All(static a => a.Value.Type?.SpecialType == SpecialType.System_String))
        {
            return arguments.Skip(1).Aggregate(
                TranslateValue(arguments[0].Value),
                (concatenated, next) => $"new {Templates}.BinaryValueTemplate({Templates}.ValueOperator.Concat, {concatenated}, {TranslateValue(next.Value)})");
        }

        if (invocation.Instance is not { } instance)
        {
            return null;
        }

        // x.Count.ToString() and x.Discount.ToString(): integers format the same in C# and SQL.
        if (method.Name == "ToString" && arguments.Length == 0 && Underlying(instance.Type)?.SpecialType
            is SpecialType.System_String or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte)
        {
            return Function("ToString", instance);
        }

        if (instance.Type?.SpecialType != SpecialType.System_String)
        {
            return null;
        }

        static bool Are(ImmutableArray<IArgumentOperation> arguments, SpecialType type)
            => arguments.All(a => a.Value.Type?.SpecialType == type);

        return method.Name switch
        {
            "ToUpper" or "ToLower" or "Trim" or "TrimStart" or "TrimEnd" when arguments.Length == 0 => Function(method.Name, instance),
            "Substring" when arguments.Length is 1 or 2 && Are(arguments, SpecialType.System_Int32)
                => Function(method.Name, [instance, .. arguments.Select(static a => a.Value)]),
            "Replace" when arguments.Length == 2 && (Are(arguments, SpecialType.System_String) || Are(arguments, SpecialType.System_Char))
                => Function(method.Name, instance, arguments[0].Value, arguments[1].Value),
            "IndexOf" when arguments.Length == 1 && arguments[0].Value.Type?.SpecialType is SpecialType.System_String or SpecialType.System_Char
                => Function(method.Name, instance, arguments[0].Value),
            _ => null,
        };
    }

    private string Function(string function, params IOperation[] arguments)
        => $"new {Templates}.FunctionValueTemplate({Templates}.ValueFunction.{function}, {string.Join(", ", arguments.Select(TranslateValue))})";

    // The condition of ?: as a predicate; one that does not read the row is decided by its value.
    private string TranslateCondition(IOperation condition)
    {
        var syntax = (ExpressionSyntax)condition.Syntax;
        _valueDepth++;
        try
        {
            return TranslatePredicate(syntax, path: "");
        }
        finally
        {
            _valueDepth--;
        }
    }

    private string TranslateConversion(IConversionOperation conversion)
    {
        if (conversion.Conversion.IsUserDefined || conversion.OperatorMethod is not null)
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, conversion.Syntax);
        }

        var operand = conversion.Operand;
        var target = Underlying(conversion.Type);
        var source = Underlying(operand.Type);

        // Lifting to nullable, a conversion to the same type, and an implicit reference or boxing conversion, such as
        // to object for ==, change nothing in SQL.
        if (target is null || source is null || SymbolEqualityComparer.Default.Equals(target, source))
        {
            return TranslateValue(operand);
        }

        if (conversion.IsImplicit
            && (conversion.Conversion.IsReference || target.SpecialType == SpecialType.System_Object || target.TypeKind == TypeKind.Interface))
        {
            return TranslateValue(operand);
        }

        if (!IsConvertible(target) || !IsConvertible(source))
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, conversion.Syntax);
        }

        return $"new {Templates}.ConvertValueTemplate({TranslateValue(operand)}, typeof({TypeName(target, conversion.Syntax)}))";
    }

    private string TranslateBinary(IBinaryOperation binary)
    {
        if (binary.OperatorMethod is { } method && !IsDecimal(method.ContainingType))
        {
            throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary.Syntax);
        }

        if (binary.OperatorKind == BinaryOperatorKind.Add && binary.Type?.SpecialType == SpecialType.System_String)
        {
            return $"new {Templates}.BinaryValueTemplate({Templates}.ValueOperator.Concat, {ConcatOperand(binary.LeftOperand)}, {ConcatOperand(binary.RightOperand)})";
        }

        var op = binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => "Add",
            BinaryOperatorKind.Subtract => "Subtract",
            BinaryOperatorKind.Multiply => "Multiply",
            BinaryOperatorKind.Divide => "Divide",
            BinaryOperatorKind.Remainder => "Modulo",
            BinaryOperatorKind.And => "BitwiseAnd",
            BinaryOperatorKind.Or => "BitwiseOr",
            BinaryOperatorKind.ExclusiveOr => "ExclusiveOr",
            _ => throw Fail(DiagnosticDescriptors.ExpressionNotSupported, binary.Syntax),
        };

        return $"new {Templates}.BinaryValueTemplate({Templates}.ValueOperator.{op}, {TranslateValue(binary.LeftOperand)}, {TranslateValue(binary.RightOperand)})";
    }

    // A string concatenation's operand. C# formats other types with ToString, which SQL matches only for integers.
    private string ConcatOperand(IOperation operand)
    {
        var value = operand is IConversionOperation { IsImplicit: true, Type.SpecialType: SpecialType.System_Object } boxing
            ? boxing.Operand
            : operand;
        var type = Underlying(value.Type);
        if (type?.SpecialType == SpecialType.System_String || !ReferencesParameter(value.Syntax))
        {
            return TranslateValue(value);
        }

        return type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte
            ? $"new {Templates}.ConvertValueTemplate({TranslateValue(value)}, typeof(string))"
            : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, operand.Syntax);
    }

    // A value slot: read from the tree where the value is a variable or constant there, otherwise computed in C#.
    private string Slot(ExpressionSyntax value, ITypeSymbol? convertedTo)
        => $"new {Templates}.SlotValueTemplate({AddValue(value, path: null, convertedTo)})";

    /// <summary>
    /// A boolean the predicate tests as a value: a coalesce, a conditional, an exclusive or, a conversion or the
    /// <c>Value</c> of a nullable property, that reads the row.
    /// </summary>
    private bool IsComputedBoolean(ExpressionSyntax node)
        => IsBoolean(node) && ReferencesParameter(node) && _model.GetOperation(node, _cancellationToken) switch
        {
            ICoalesceOperation or IConditionalOperation => true,
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ExclusiveOr } => true,
            IConversionOperation { Conversion.IsUserDefined: false } => true,
            IPropertyReferenceOperation { Property.Name: ValueProperty, Instance: { } instance } => IsNullableValue(instance.Type),
            _ => false,
        };

    /// <summary>
    /// The operand a comparison reads as one property, as the simple templates compare it: an entity property,
    /// through reference navigations, or a value of a group; null for anything else.
    /// </summary>
    private PredicateOperand? SimpleOperand(ExpressionSyntax node)
    {
        node = StripParentheses(node);
        if (_operands is not null)
        {
            return _operands(node);
        }

        if (node is not MemberAccessExpressionSyntax member || PropertyPath(member) is not { } chain
            || !_allowNavigations && chain.Count > 1 || !EntityShapeBuilder.IsReadableScalar(chain[chain.Count - 1].Type))
        {
            return null;
        }

        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (EntityShapeBuilder.IsReadableScalar(chain[i].Type)
                || EntityShapeBuilder.IsCollectionNavigation(chain[i].Type)
                || EntityShapeBuilder.NavigationTarget(chain[i].Type) is null)
            {
                return null;
            }
        }

        return EntityProperty(node);
    }

    private bool IsBoolean(ExpressionSyntax node)
        => _model.GetTypeInfo(node, _cancellationToken).Type?.SpecialType == SpecialType.System_Boolean;

    private static bool IsCondition(BinaryOperatorKind kind)
        => kind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual
            or BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr;

    // & and | on booleans are conditions; on integers they are bitwise.
    private static bool IsLogical(IBinaryOperation binary)
        => binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or
            && Underlying(binary.Type)?.SpecialType == SpecialType.System_Boolean;

    private static bool IsNullableValue(ITypeSymbol? type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static ITypeSymbol? Underlying(ITypeSymbol? type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private static bool IsDecimal(ITypeSymbol? type) => type?.SpecialType == SpecialType.System_Decimal;

    // The types SQL casts between: numbers, enums, booleans, strings and dates.
    private static bool IsConvertible(ITypeSymbol type)
        => type.TypeKind == TypeKind.Enum || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_Int64
            or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_String
            or SpecialType.System_DateTime;

    private string TypeName(ITypeSymbol type, SyntaxNode syntax)
        => _model.Compilation.IsSymbolAccessibleWithin(type, _model.Compilation.Assembly)
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : throw Fail(DiagnosticDescriptors.ExpressionNotSupported, syntax);

    /// <summary>
    /// The entity property <paramref name="node"/> reads directly (<c>x.Prop</c>), or the operand it is; null when it
    /// does not involve the lambda parameter. Other uses of the parameter are diagnostics.
    /// </summary>
    private PredicateOperand? EntityProperty(ExpressionSyntax node)
    {
        node = StripParentheses(node);
        if (!ReferencesParameter(node))
        {
            return null;
        }

        if (_operands is not null)
        {
            return _operands(node) ?? throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
        }

        if (node is MemberAccessExpressionSyntax member)
        {
            if (_allowNavigations && PropertyPath(member) is { } chain)
            {
                return ThroughNavigations(chain, node);
            }

            if (IsParameter(member.Expression))
            {
                if (_model.GetSymbolInfo(member, _cancellationToken).Symbol is not IPropertySymbol property)
                {
                    throw Fail(DiagnosticDescriptors.ExpressionNotSupported, node);
                }

                return EntityShapeBuilder.IsReadableScalar(property.Type)
                    ? new PredicateOperand(property.Name, property.Type)
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

    /// <summary>
    /// The property path a chain of reference navigations ends with, such as <c>Blog.Name</c> for <c>x.Blog.Name</c>.
    /// A collection on the way, a member of a scalar, or a chain that ends with a navigation is a diagnostic.
    /// </summary>
    private PredicateOperand ThroughNavigations(IReadOnlyList<IPropertySymbol> chain, ExpressionSyntax node)
    {
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var type = chain[i].Type;
            if (EntityShapeBuilder.IsReadableScalar(type))
            {
                // x.Name.Length, or x.Blog.Name.Length: a member of a scalar.
                throw Fail(DiagnosticDescriptors.MethodNotSupported, node);
            }

            if (EntityShapeBuilder.IsCollectionNavigation(type) || EntityShapeBuilder.NavigationTarget(type) is null)
            {
                throw Fail(DiagnosticDescriptors.CollectionNotSupported, node);
            }
        }

        var last = chain[chain.Count - 1];
        return EntityShapeBuilder.IsReadableScalar(last.Type)
            ? new PredicateOperand(string.Join(".", chain.Select(static p => p.Name)), last.Type)
            : throw Fail(
                EntityShapeBuilder.IsCollectionNavigation(last.Type) ? DiagnosticDescriptors.CollectionNotSupported : DiagnosticDescriptors.NavigationNotSupported,
                node);
    }

    /// <summary>The path of a chain of reference navigations from the parameter, such as <c>Blog</c> for <c>x.Blog</c>; otherwise null.</summary>
    private string? NavigationPath(ExpressionSyntax node)
    {
        if (!_allowNavigations || _operands is not null || StripParentheses(node) is not MemberAccessExpressionSyntax member
            || PropertyPath(member) is not { } chain)
        {
            return null;
        }

        return chain.All(static p => !EntityShapeBuilder.IsCollectionNavigation(p.Type) && EntityShapeBuilder.NavigationTarget(p.Type) is not null)
            ? string.Join(".", chain.Select(static p => p.Name))
            : null;
    }

    /// <summary>The properties a member chain reads from the lambda parameter, outermost first; null for anything else.</summary>
    private List<IPropertySymbol>? PropertyPath(MemberAccessExpressionSyntax member)
    {
        var chain = new List<IPropertySymbol>();
        ExpressionSyntax current = member;
        while (StripParentheses(current) is MemberAccessExpressionSyntax access)
        {
            if (_model.GetSymbolInfo(access, _cancellationToken).Symbol is not IPropertySymbol property)
            {
                return null;
            }

            chain.Insert(0, property);
            current = access.Expression;
        }

        return IsParameter(current) ? chain : null;
    }

    /// <summary>
    /// Records a value slot. A variable or constant outside computed values is read from its node in the tree; anything
    /// else is computed in C# from the captured variables.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="path">The tree node that holds it, when the translation knows it.</param>
    /// <param name="convertedTo">The type C# converts the value to, or <see langword="null"/>.</param>
    private int AddValue(ExpressionSyntax value, string? path, ITypeSymbol? convertedTo = null)
    {
        if (_readValue is not null)
        {
            _valuePaths.Add(_readValue(value));
        }
        else if (path is not null && _valueDepth == 0 && IsReadableValue(value))
        {
            _readsBody = true;
            _valuePaths.Add($"{CapturedValues}.Read({path})");
        }
        else
        {
            _valuePaths.Add(_writer.Write(value, convertedTo));
        }

        _ownValues++;
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

    private static string NullCheck(PredicateOperand property, bool isNull) => NullCheck(property.Name, isNull);

    private static string NullCheck(string name, bool isNull)
        => $"new {Templates}.NullCheckTemplate(\"{name}\", isNull: {(isNull ? "true" : "false")})";

    // Parentheses and the null-forgiving operator have no node in the expression tree.
    internal static ExpressionSyntax StripParentheses(ExpressionSyntax node)
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
}

/// <summary>What a predicate compares: an entity property, or a value such as an aggregate of a group, by its name in the template.</summary>
internal readonly record struct PredicateOperand(string Name, ITypeSymbol Type);

/// <summary>Ends a translation with the diagnostic that explains why the construct cannot be translated.</summary>
internal sealed class TranslationException(DiagnosticInfo diagnostic) : Exception(diagnostic.Descriptor.Id)
{
    public DiagnosticInfo Diagnostic { get; } = diagnostic;
}
