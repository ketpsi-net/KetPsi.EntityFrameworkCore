namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A <c>HasQueryFilter</c> call translated at build time.</summary>
/// <param name="ContextType">The context whose model declares the filter, or <c>DbContext</c> when it is not known.</param>
/// <param name="EntityType">The filtered entity type.</param>
/// <param name="FilterKey">The filter's name, or <see langword="null"/> for an unnamed filter.</param>
/// <param name="TemplateCode">C# that constructs the predicate template.</param>
/// <param name="ValueCodes">For each value slot, C# that reads the value from the context parameter.</param>
/// <param name="Accessors">Accessors for private context members the values read.</param>
internal sealed record QueryFilterModel(
    string ContextType,
    string EntityType,
    string? FilterKey,
    string TemplateCode,
    EquatableArray<string> ValueCodes,
    EquatableArray<AccessorModel> Accessors);

/// <summary>An <c>UnsafeAccessor</c> for a context member the generated code cannot access directly.</summary>
/// <param name="Name">The accessor method name, unique within its filter.</param>
/// <param name="IsField">Whether the member is a field; otherwise it is a property, read through its getter.</param>
/// <param name="MemberName">The field or property name.</param>
/// <param name="DeclaringType">The fully qualified type that declares the member.</param>
/// <param name="ValueType">The fully qualified member type.</param>
internal sealed record AccessorModel(string Name, bool IsField, string MemberName, string DeclaringType, string ValueType);

/// <summary>What one model configuration call contributes: a query filter, or a diagnostic.</summary>
internal sealed record ModelConfiguration(QueryFilterModel? Filter, DiagnosticInfo? Diagnostic);
