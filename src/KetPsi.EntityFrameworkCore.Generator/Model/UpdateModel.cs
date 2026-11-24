namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>An <c>ExecuteUpdate</c> call: its setters translated, or recorded when they are a delegate.</summary>
/// <param name="Setters">
/// For EF Core 9's expression of setters, each <c>SetProperty</c> translated; their values read <c>captures</c>, the
/// variables the whole expression captures.
/// </param>
/// <param name="RecordsSetters">
/// Whether the setters are a delegate, as EF Core 10 and later take them: the interceptor runs it, and the interceptors
/// of its <c>SetProperty</c> calls record their setters.
/// </param>
internal sealed record UpdateModel(EquatableArray<SetterModel> Setters, bool RecordsSetters);

/// <summary>One <c>SetProperty</c>: the property it sets and the value template it sets it to.</summary>
/// <param name="PropertyName">The property of the updated entity.</param>
/// <param name="TemplateCode">C# that constructs the value template.</param>
/// <param name="ValueCodes">C# that computes each value slot of the template.</param>
/// <param name="ReadsCaptures">Whether a value code reads <c>captures</c>.</param>
internal sealed record SetterModel(string PropertyName, string TemplateCode, EquatableArray<string> ValueCodes, bool ReadsCaptures);
