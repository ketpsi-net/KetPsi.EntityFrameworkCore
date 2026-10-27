using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>A source location as plain data, so pipeline models don't hold on to syntax trees.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}
