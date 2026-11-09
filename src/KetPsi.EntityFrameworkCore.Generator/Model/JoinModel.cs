namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A <c>Join</c>'s keys, as the runtime's join node takes them; its result selector is the call's projection.</summary>
/// <param name="OuterKeys">The paths of the outer key parts, of the queried entity or an earlier join's.</param>
/// <param name="InnerKeys">The inner entity's key properties, one for each outer key part.</param>
/// <param name="InnerType">The fully qualified inner entity type.</param>
internal sealed record JoinModel(EquatableArray<string> OuterKeys, EquatableArray<string> InnerKeys, string InnerType);
