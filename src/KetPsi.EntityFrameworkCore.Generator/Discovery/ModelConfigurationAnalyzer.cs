using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Reads EF Core model configuration: translates <c>HasQueryFilter</c>, and reports the mappings milestone 1 does
/// not support. This runs in the project that configures the model, which may not be the one that queries it.
/// </summary>
internal static class ModelConfigurationAnalyzer
{
    private const string EntityFrameworkNamespace = "Microsoft.EntityFrameworkCore";
    private const string HasQueryFilter = "HasQueryFilter";
    private const string OwnedAttribute = "Microsoft.EntityFrameworkCore.OwnedAttribute";
    private const string ComplexTypeAttribute = "Microsoft.EntityFrameworkCore.ComplexTypeAttribute";
    private const string DataAnnotationsComplexTypeAttribute = "System.ComponentModel.DataAnnotations.Schema.ComplexTypeAttribute";

    private static readonly HashSet<string> ConverterMethods = new(StringComparer.Ordinal) { "HasConversion", "HaveConversion" };

    private static readonly HashSet<string> OwnedOrComplexMethods = new(StringComparer.Ordinal)
    {
        "OwnsOne", "OwnsMany", "ComplexProperty", "ComplexCollection", "ToJson",
    };

    public static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken) => node switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Name.Identifier.ValueText is var name
            && (name == HasQueryFilter || ConverterMethods.Contains(name) || OwnedOrComplexMethods.Contains(name)),
        ClassDeclarationSyntax declaration => declaration.AttributeLists.Count > 0,
        _ => false,
    };

    public static ModelConfiguration? Analyze(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        => Analyze(context.Node, context.SemanticModel, cancellationToken);

    public static ModelConfiguration? Analyze(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        try
        {
            return node switch
            {
                ClassDeclarationSyntax declaration => AnalyzeClass(declaration, model, cancellationToken),
                InvocationExpressionSyntax invocation => AnalyzeInvocation(invocation, model, cancellationToken),
                _ => null,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Reject(DiagnosticDescriptors.InternalError, node, exception.Message);
        }
    }

    private static ModelConfiguration? AnalyzeClass(ClassDeclarationSyntax declaration, SemanticModel model, CancellationToken cancellationToken)
    {
        var type = model.GetDeclaredSymbol(declaration, cancellationToken);
        var isOwnedOrComplex = type?.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString()
            is OwnedAttribute or ComplexTypeAttribute or DataAnnotationsComplexTypeAttribute) == true;

        return isOwnedOrComplex
            ? new ModelConfiguration(null, DiagnosticInfo.Create(
                DiagnosticDescriptors.OwnedOrComplexNotSupported, LocationInfo.From(declaration.Identifier.GetLocation()), type!.Name))
            : null;
    }

    private static ModelConfiguration? AnalyzeInvocation(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || method.ContainingNamespace?.ToDisplayString().StartsWith(EntityFrameworkNamespace, StringComparison.Ordinal) != true)
        {
            return null;
        }

        // Points at the method name, without type arguments.
        var name = LocationInfo.From(((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.GetLocation());
        if (ConverterMethods.Contains(method.Name))
        {
            return new ModelConfiguration(null, DiagnosticInfo.Create(DiagnosticDescriptors.ValueConverterNotSupported, name, method.Name));
        }

        if (OwnedOrComplexMethods.Contains(method.Name))
        {
            return new ModelConfiguration(null, DiagnosticInfo.Create(DiagnosticDescriptors.OwnedOrComplexNotSupported, name, method.Name));
        }

        return method.Name == HasQueryFilter ? AnalyzeQueryFilter(invocation, method, model, cancellationToken) : null;
    }

    private static ModelConfiguration AnalyzeQueryFilter(
        InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model, CancellationToken cancellationToken)
    {
        // The generic EntityTypeBuilder<T> knows the entity type; the non-generic one takes an untyped lambda.
        if (method.ContainingType.TypeArguments.FirstOrDefault() is not INamedTypeSymbol entity
            || model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return Reject(DiagnosticDescriptors.QueryFilterNotTranslatable, invocation, "use the generic EntityTypeBuilder<TEntity>");
        }

        string? filterKey = null;
        LambdaExpressionSyntax? lambda = null;
        foreach (var argument in operation.Arguments)
        {
            var syntax = argument.Value.Syntax as ExpressionSyntax;
            if (argument.Parameter?.Type.SpecialType == SpecialType.System_String)
            {
                if (syntax is null || model.GetConstantValue(syntax, cancellationToken) is not { HasValue: true, Value: string key })
                {
                    return Reject(DiagnosticDescriptors.QueryFilterNotTranslatable, argument.Value.Syntax, "the filter name must be a constant");
                }

                filterKey = key;
            }
            else if (syntax is not null)
            {
                lambda = PredicateTranslator.StripParentheses(syntax) as LambdaExpressionSyntax;
                if (lambda is null)
                {
                    return Reject(DiagnosticDescriptors.QueryFilterNotTranslatable, syntax, "the filter must be a lambda written in the call");
                }
            }
        }

        if (lambda is null)
        {
            return Reject(DiagnosticDescriptors.QueryFilterNotTranslatable, invocation, "the filter must be a lambda written in the call");
        }

        var contextType = EnclosingContext(invocation, model, cancellationToken);
        var reader = new ContextValueReader(model, contextType, cancellationToken);
        var (predicate, diagnostic) = PredicateTranslator.Translate(lambda, model, cancellationToken, reader.Read);
        if (diagnostic is not null)
        {
            return new ModelConfiguration(null, diagnostic);
        }

        return new ModelConfiguration(
            new QueryFilterModel(
                contextType is null ? "global::" + KnownTypes.DbContext : EntityDiscovery.TypeName(contextType),
                EntityDiscovery.TypeName(entity),
                filterKey,
                predicate!.TemplateCode,
                predicate.ValuePaths,
                reader.Accessors.ToEquatableArray()),
            null);
    }

    // OnModelCreating runs on a context; the filter belongs to that context type. Configuration classes are
    // shared, so their filters apply to every context.
    private static INamedTypeSymbol? EnclosingContext(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        var declaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var dbContext = model.Compilation.GetTypeByMetadataName(KnownTypes.DbContext);
        if (declaration is null || dbContext is null || model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type)
        {
            return null;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, dbContext))
            {
                return type;
            }
        }

        return null;
    }

    private static ModelConfiguration Reject(DiagnosticDescriptor descriptor, SyntaxNode node, string argument)
        => new(null, DiagnosticInfo.Create(descriptor, LocationInfo.From(node.GetLocation()), argument));
}
