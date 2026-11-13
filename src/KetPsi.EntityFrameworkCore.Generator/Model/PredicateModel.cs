namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A predicate lambda, or a computed order key, translated at build time.</summary>
/// <param name="TemplateCode">C# that constructs the predicate or value template.</param>
/// <param name="ValueCodes">
/// For each value slot, C# that reads the value: from the expression tree node that holds it, starting at the lambda body
/// (the variable <c>body</c>); or computed from the variables the lambda captures (the variable <c>captures</c>).
/// </param>
/// <param name="ReadsBody">Whether a value is read from the lambda body.</param>
/// <param name="ReadsCaptures">Whether a value is computed from the captured variables.</param>
internal sealed record PredicateModel(string TemplateCode, EquatableArray<string> ValueCodes, bool ReadsBody = false, bool ReadsCaptures = false);
