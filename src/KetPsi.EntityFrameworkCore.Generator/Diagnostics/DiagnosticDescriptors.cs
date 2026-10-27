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

    public static readonly DiagnosticDescriptor IncludeNotSupported = Error(
        "KP1002",
        "Include not supported",
        "'{0}' is not supported yet. Load related entities with separate queries.");

    public static readonly DiagnosticDescriptor TrackingNotSupported = Error(
        "KP1003",
        "Tracking query not supported",
        "'{0}' is not supported. KetPsi.EntityFrameworkCore only runs no-tracking queries; use AsNoTracking().");

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

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat)
        => new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
