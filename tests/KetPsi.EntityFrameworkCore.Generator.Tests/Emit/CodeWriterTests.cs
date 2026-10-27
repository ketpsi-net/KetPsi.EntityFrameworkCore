using KetPsi.EntityFrameworkCore.Generator.Emit;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class CodeWriterTests
{
    [Fact]
    public void OpenAndClose_IndentWithFourSpacesAndLf()
    {
        var text = new CodeWriter()
            .Open("namespace N")
            .Open("class C")
            .Line("int x;")
            .Close()
            .Close(";")
            .ToString();

        Assert.Equal("namespace N\n{\n    class C\n    {\n        int x;\n    }\n};\n", text);
    }

    [Fact]
    public void EmptyLine_HasNoIndentation()
        => Assert.Equal("a\n{\n\n}\n", new CodeWriter().Open("a").Line().Close().ToString());
}
