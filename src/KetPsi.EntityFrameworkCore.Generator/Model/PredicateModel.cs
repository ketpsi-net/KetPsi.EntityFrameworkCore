namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A predicate lambda translated at build time.</summary>
/// <param name="TemplateCode">C# that constructs the predicate template.</param>
/// <param name="ValuePaths">
/// For each value slot, C# that navigates from the lambda body (the variable <c>body</c>) to the expression tree
/// node holding the value.
/// </param>
internal sealed record PredicateModel(string TemplateCode, EquatableArray<string> ValuePaths);
