using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Diagnostics;

/// <summary>
/// Every diagnostic the generator reports. All are errors: a query the generator cannot translate must not
/// fall back to EF Core's runtime pipeline.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "KetPsi.EntityFrameworkCore";

    public static readonly DiagnosticDescriptor InternalError = Error(
        "KP0001",
        "Generator failure",
        "KetPsi.EntityFrameworkCore.Generator failed while analyzing this call: {0}");

    public static readonly DiagnosticDescriptor InterceptorsNotEnabled = Error(
        "KP0002",
        "Interceptors are not enabled",
        "Interceptors are not enabled for the namespace '{0}'. Add it to the InterceptorsNamespaces MSBuild property, " +
        "which the KetPsi.EntityFrameworkCore.Generator package does when it is referenced as a package.");

    public static readonly DiagnosticDescriptor UnsupportedOperator = Error(
        "KP1001",
        "Query operator not supported",
        "The query operator '{0}' is not supported by KetPsi.EntityFrameworkCore");

    public static readonly DiagnosticDescriptor IncludePathNotSupported = Error(
        "KP1002",
        "Include path not supported",
        "The include path '{0}' is not supported: {1}");

    public static readonly DiagnosticDescriptor RawSqlNotSupported = Error(
        "KP1004",
        "Raw SQL not supported",
        "Raw SQL ('{0}') is not supported by KetPsi.EntityFrameworkCore");

    public static readonly DiagnosticDescriptor LambdaRequired = Error(
        "KP1005",
        "Lambda expression required",
        "The '{0}' argument of '{1}' must be a lambda expression written at the call site, not a variable or a method");

    public static readonly DiagnosticDescriptor UnsupportedOverload = Error(
        "KP1006",
        "Operator overload not supported",
        "This overload of '{0}' is not supported: {1}");

    public static readonly DiagnosticDescriptor ExpressionNotSupported = Error(
        "KP2001",
        "Expression not supported",
        "The expression '{0}' cannot be translated to SQL by KetPsi.EntityFrameworkCore. Predicates compare entity " +
        "properties with values and combine the comparisons with !, && and ||.");

    public static readonly DiagnosticDescriptor NavigationNotSupported = Error(
        "KP2002",
        "Navigation not supported",
        "'{0}' uses a navigation, which is not supported yet");

    public static readonly DiagnosticDescriptor MethodNotSupported = Error(
        "KP2003",
        "Method or member not supported",
        "'{0}' calls a method or member that cannot be translated to SQL yet");

    public static readonly DiagnosticDescriptor DbFunctionsNotSupported = Error(
        "KP2004",
        "EF.Functions not supported",
        "'{0}' uses EF.Functions, which is not supported yet");

    public static readonly DiagnosticDescriptor CollectionNotSupported = Error(
        "KP2005",
        "Collection query not supported",
        "'{0}' queries a related collection, which is not supported yet");

    public static readonly DiagnosticDescriptor MemberComparisonNotSupported = Error(
        "KP2006",
        "Property comparison not supported",
        "'{0}' compares two entity properties, which is not supported yet");

    public static readonly DiagnosticDescriptor ValueNotSupported = Error(
        "KP2007",
        "Value not supported",
        "The value '{0}' must be a constant, a variable, or a field or property of one. Compute it into a local " +
        "variable before the query.");

    public static readonly DiagnosticDescriptor OrderKeyNotSupported = Error(
        "KP2008",
        "Order key not supported",
        "The order key '{0}' must be a property of the entity itself, such as x => x.Name");

    public static readonly DiagnosticDescriptor PropertyTypeNotSupported = Error(
        "KP3001",
        "Property type not supported",
        "The property '{0}.{1}' has the type '{2}', which KetPsi.EntityFrameworkCore cannot read yet. " +
        "Mark it [NotMapped] if EF Core does not map it.");

    public static readonly DiagnosticDescriptor ValueConverterNotSupported = Error(
        "KP3002",
        "Value converter not supported",
        "'{0}' configures a value converter, which KetPsi.EntityFrameworkCore does not support yet");

    public static readonly DiagnosticDescriptor OwnedOrComplexNotSupported = Error(
        "KP3003",
        "Owned, complex or JSON mapping not supported",
        "'{0}' configures an owned type, a complex type or a JSON column, which KetPsi.EntityFrameworkCore does not support yet");

    public static readonly DiagnosticDescriptor QueryFilterNotTranslatable = Error(
        "KP3005",
        "Query filter not translatable",
        "The query filter cannot be translated at build time: {0}");

    public static readonly DiagnosticDescriptor EntityNotConstructible = Error(
        "KP3004",
        "Entity cannot be constructed",
        "The entity type '{0}' needs a non-abstract class with a parameterless constructor, accessible to the generated " +
        "code when the entity has init-only properties");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat)
        => new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
