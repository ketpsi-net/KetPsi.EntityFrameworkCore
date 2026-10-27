using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KetPsi.EntityFrameworkCore.Generator.Discovery;

/// <summary>
/// Turns a query filter value into C# that reads it from the context a query runs on: constants, static members,
/// and members of the context, like EF Core, which evaluates context members per query.
/// </summary>
internal sealed class ContextValueReader(SemanticModel model, INamedTypeSymbol? context, CancellationToken cancellationToken)
{
    /// <summary>The name of the context parameter in the generated value reader.</summary>
    public const string ContextParameter = "db";

    /// <summary>Stands for the filter's accessor prefix, which the emitter fills in.</summary>
    public const string AccessorPrefix = "__KETPSI_FILTER__";

    private const string FieldValueReason =
        "the value '{0}' is not available when a query runs; use a constant, a static member or a member of the context";

    public List<AccessorModel> Accessors { get; } = [];

    private string Root => $"(({EntityDiscovery.TypeName(context!)}){ContextParameter})";

    public string Read(ExpressionSyntax value)
    {
        var node = PredicateTranslator.StripParentheses(value);
        var constant = model.GetConstantValue(node, cancellationToken);
        if (constant.HasValue)
        {
            return Literal(constant.Value, model.GetTypeInfo(node, cancellationToken).Type);
        }

        return ReadMember(node) ?? throw new TranslationException(DiagnosticInfo.Create(
            DiagnosticDescriptors.QueryFilterNotTranslatable,
            LocationInfo.From(value.GetLocation()),
            string.Format(System.Globalization.CultureInfo.InvariantCulture, FieldValueReason, value)));
    }

    private string? ReadMember(ExpressionSyntax node)
    {
        switch (PredicateTranslator.StripParentheses(node))
        {
            case ThisExpressionSyntax when context is not null:
                return Root;

            case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier, cancellationToken).Symbol is IFieldSymbol or IPropertySymbol:
                var member = model.GetSymbolInfo(identifier, cancellationToken).Symbol!;
                if (member.IsStatic)
                {
                    return Static(member);
                }

                return IsContextMember(member) ? Member(Root, member, fromContext: true) : null;

            case MemberAccessExpressionSyntax access when model.GetSymbolInfo(access, cancellationToken).Symbol is IFieldSymbol or IPropertySymbol:
                var accessed = model.GetSymbolInfo(access, cancellationToken).Symbol!;
                if (accessed.IsStatic)
                {
                    return Static(accessed);
                }

                var receiver = PredicateTranslator.StripParentheses(access.Expression);
                if (receiver is ThisExpressionSyntax && context is not null)
                {
                    return Member(Root, accessed, fromContext: true);
                }

                return ReadMember(receiver) is { } receiverCode ? Member(receiverCode, accessed, fromContext: false) : null;

            case CastExpressionSyntax cast
                when model.GetOperation(cast, cancellationToken) is IConversionOperation { Conversion.IsUserDefined: false } conversion
                    && ReadMember(cast.Expression) is { } operand:
                return $"(({EntityDiscovery.TypeName(conversion.Type!)}){operand})";

            default:
                return null;
        }
    }

    private bool IsContextMember(ISymbol member)
    {
        for (var type = context; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, member.ContainingType))
            {
                return true;
            }
        }

        return false;
    }

    private string? Static(ISymbol member)
        => model.Compilation.IsSymbolAccessibleWithin(member, model.Compilation.Assembly)
            ? $"{EntityDiscovery.TypeName(member.ContainingType)}.{member.Name}"
            : null;

    // Private members of the context are common (a tenant field); they are read through an UnsafeAccessor.
    private string? Member(string receiver, ISymbol member, bool fromContext)
    {
        if (model.Compilation.IsSymbolAccessibleWithin(member, model.Compilation.Assembly))
        {
            return $"{receiver}.{member.Name}";
        }

        if (!fromContext)
        {
            return null;
        }

        var name = $"{AccessorPrefix}Value{Accessors.Count}";
        var type = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
        Accessors.Add(new AccessorModel(
            name,
            member is IFieldSymbol,
            member.Name,
            EntityDiscovery.TypeName(member.ContainingType),
            EntityDiscovery.TypeName(type)));
        return $"{name}({receiver})";
    }

    private static string Literal(object? value, ITypeSymbol? type)
    {
        switch (value)
        {
            case null:
                return "null";
            case string or char or bool:
                return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
            case int when type is null || type.SpecialType == SpecialType.System_Int32:
                return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
            default:
                // Keeps the constant's type, such as an enum or long, rather than the literal's.
                var typeName = type is null ? "object" : EntityDiscovery.TypeName(type);
                return $"(({typeName})({SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)}))";
        }
    }
}
