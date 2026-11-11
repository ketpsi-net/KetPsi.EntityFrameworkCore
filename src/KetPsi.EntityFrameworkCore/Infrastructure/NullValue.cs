using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// What the generated readers throw for a null read into a non-nullable value type: the running .NET's own message
/// for reading <see cref="Nullable{T}.Value"/> without a value, which is what EF Core's materializers throw.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NullValue
{
    /// <summary>The message of the <see cref="InvalidOperationException"/> that <c>Nullable&lt;T&gt;.Value</c> throws.</summary>
    public static string Message => ExceptionMessages.NullableObjectMustHaveAValue;
}
