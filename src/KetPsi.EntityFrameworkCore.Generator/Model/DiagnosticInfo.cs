using Microsoft.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A diagnostic to report, as plain data.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] messageArgs)
        => new(descriptor, location, messageArgs.ToEquatableArray());

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, [.. MessageArgs]);
}
