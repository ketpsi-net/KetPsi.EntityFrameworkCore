using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Translates the setters of <c>ExecuteUpdate</c>: each <c>SetProperty</c> becomes the property it sets and a value
/// template the database computes, from the row's columns and the values the call captures.
/// </summary>
/// <remarks>
/// EF Core 9 takes the setters as an expression, a chain of <c>SetProperty</c> calls, which is translated as a whole.
/// EF Core 10 and later take a delegate, which can set properties in <c>if</c> statements and loops, so each
/// <c>SetProperty</c> call in it is translated and intercepted on its own.
/// </remarks>
internal static class UpdateTranslator
{
    public const string SetPropertyCallsType = "Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<TSource>";
    public const string SettersBuilderType = "Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<TSource>";

    private const string Templates = "global::KetPsi.EntityFrameworkCore.Infrastructure.Templates";
    private const string ReasonChain = "the setters must be SetProperty calls chained on the lambda's parameter";
    private const string ReasonProperty =
        "the set property must be a mapped property of the updated entity itself, such as o => o.Name, not one through a navigation";
    private const string ReasonLambda = "the property must be a lambda written at the call, such as o => o.Name";

    /// <summary>The setters of EF Core 9's <c>s =&gt; s.SetProperty(...).SetProperty(...)</c>, in order.</summary>
    public static (EquatableArray<SetterModel> Setters, DiagnosticInfo? Diagnostic) TranslateExpression(
        LambdaExpressionSyntax lambda, INamedTypeSymbol entity, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        if (Parameter(lambda, model, cancellationToken) is not { } parameter || lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation)
        {
            return (default, Diagnostic(lambda, ReasonChain));
        }

        // The calls from the parameter outwards.
        var calls = new List<IInvocationOperation>();
        while (Unwrapped(operation) is IInvocationOperation { TargetMethod.Name: "SetProperty", Instance: { } instance } call
            && call.TargetMethod.ContainingType.OriginalDefinition.ToDisplayString() == SetPropertyCallsType)
        {
            calls.Insert(0, call);
            operation = instance;
        }

        if (Unwrapped(operation) is not IParameterReferenceOperation { Parameter: var root } || !SymbolEqualityComparer.Default.Equals(root, parameter)
            || calls.Count == 0)
        {
            return (default, Diagnostic(lambda, ReasonChain));
        }

        var properties = EntityShapeBuilder.Build(entity, model.Compilation, location).Properties;
        var writer = new CapturedValueWriter(model, parameter, cancellationToken);
        var setters = new List<SetterModel>();
        try
        {
            foreach (var call in calls)
            {
                var name = PropertyName(call.Arguments[0].Value, properties, model, cancellationToken);
                var value = call.Arguments[1].Value;

                // A value lambda is computed in SQL; any other value is computed in C#, in the property's type.
                string template;
                List<string> codes;
                if (Lambda(value) is { } valueLambda)
                {
                    codes = [];
                    template = PredicateTranslator.TranslateProjectedValue(
                        (ExpressionSyntax)valueLambda.Body, Parameter(valueLambda, model, cancellationToken)!, model, codes, cancellationToken);
                }
                else
                {
                    template = $"new {Templates}.SlotValueTemplate(0)";
                    codes = [writer.Write((ExpressionSyntax)Unwrapped(value).Syntax, call.TargetMethod.Parameters[1].Type)];
                }

                // The values are numbered among the variables the whole expression captures, as ReadAll reads them.
                var resolved = codes.Select(code => writer.ResolveCaptures(code, body)).ToList();
                setters.Add(new SetterModel(name, template, resolved.ToEquatableArray(), resolved.Exists(IsCapturing)));
            }
        }
        catch (TranslationException exception)
        {
            return (default, exception.Diagnostic);
        }

        return (setters.ToEquatableArray(), null);
    }

    /// <summary>
    /// One <c>SetProperty</c> call of EF Core 10's setters builder: a value lambda is computed in SQL, its values read
    /// from the variables it captures; any other value is the call's argument.
    /// </summary>
    /// <param name="call">The <c>SetProperty</c> call.</param>
    /// <param name="entity">The updated entity.</param>
    /// <param name="valueParameter">The name of the interceptor's value parameter.</param>
    /// <param name="model">The semantic model of the call's tree.</param>
    /// <param name="location">Where diagnostics about the entity point.</param>
    /// <param name="cancellationToken">Cancels the translation.</param>
    public static (SetterModel? Setter, DiagnosticInfo? Diagnostic) TranslateCall(
        IInvocationOperation call, INamedTypeSymbol entity, string valueParameter, SemanticModel model, LocationInfo location, CancellationToken cancellationToken)
    {
        var properties = EntityShapeBuilder.Build(entity, model.Compilation, location).Properties;
        try
        {
            var name = PropertyName(call.Arguments[0].Value, properties, model, cancellationToken);
            if (Lambda(call.Arguments[1].Value) is { } valueLambda && IsExpression(call.TargetMethod.Parameters[1].Type))
            {
                var value = PredicateTranslator.TranslateSetterValue(valueLambda, model, cancellationToken);
                return (new SetterModel(name, value.TemplateCode, value.ValueCodes, value.ReadsCaptures), null);
            }

            return (new SetterModel(name, $"new {Templates}.SlotValueTemplate(0)", new[] { valueParameter }.ToEquatableArray(), ReadsCaptures: false), null);
        }
        catch (TranslationException exception)
        {
            return (null, exception.Diagnostic);
        }
    }

    // The property a selector such as o => o.Name sets: a mapped property of the entity itself.
    private static string PropertyName(IOperation selector, EquatableArray<PropertyShape> properties, SemanticModel model, CancellationToken cancellationToken)
    {
        if (Lambda(selector) is not { } lambda)
        {
            throw new TranslationException(Diagnostic(selector.Syntax, ReasonLambda));
        }

        var parameter = Parameter(lambda, model, cancellationToken);
        if (lambda.Body is not ExpressionSyntax body
            || model.GetOperation(body, cancellationToken) is not { } operation
            || Unwrapped(operation) is not IPropertyReferenceOperation { Instance: var instance, Property.Name: var name }
            || Unwrapped(instance!) is not IParameterReferenceOperation { Parameter: var root }
            || !SymbolEqualityComparer.Default.Equals(root, parameter)
            || !properties.Any(p => p.Name == name))
        {
            throw new TranslationException(Diagnostic(lambda, ReasonProperty));
        }

        return name;
    }

    private static LambdaExpressionSyntax? Lambda(IOperation value)
        => Unwrapped(value) switch
        {
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Syntax: LambdaExpressionSyntax lambda } } => lambda,
            IAnonymousFunctionOperation { Syntax: LambdaExpressionSyntax lambda } => lambda,
            _ => null,
        };

    private static IParameterSymbol? Parameter(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken cancellationToken)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => model.GetDeclaredSymbol(simple.Parameter, cancellationToken) as IParameterSymbol,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized
                => model.GetDeclaredSymbol(parenthesized.ParameterList.Parameters[0], cancellationToken) as IParameterSymbol,
            _ => null,
        };

    private static bool IsExpression(ITypeSymbol type)
        => type.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>";

    private static bool IsCapturing(string code) => code.Contains(CapturedValueWriter.Captures + "[");

    private static IOperation Unwrapped(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static DiagnosticInfo Diagnostic(SyntaxNode syntax, string reason)
        => DiagnosticInfo.Create(DiagnosticDescriptors.SetterNotSupported, LocationInfo.From(syntax.GetLocation()), syntax.ToString(), reason);
}
