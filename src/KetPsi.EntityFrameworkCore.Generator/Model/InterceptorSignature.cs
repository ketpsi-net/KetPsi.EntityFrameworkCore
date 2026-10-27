namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>
/// The intercepted method with its type arguments substituted, which the interceptor must match exactly, and
/// which the interceptor calls when the query is not a KetPsi query.
/// </summary>
/// <param name="Name">The method name.</param>
/// <param name="ContainingType">The fully qualified type that declares the method.</param>
/// <param name="ReturnType">The fully qualified return type, with nullable annotations.</param>
/// <param name="Parameters">The parameters; the first is the query, declared with <c>this</c>.</param>
internal sealed record InterceptorSignature(
    string Name,
    string ContainingType,
    string ReturnType,
    EquatableArray<ParameterSignature> Parameters);

internal sealed record ParameterSignature(string Type, string Name);
