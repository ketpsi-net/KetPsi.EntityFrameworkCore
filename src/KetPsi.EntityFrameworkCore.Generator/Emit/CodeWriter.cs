using System.Text;

namespace KetPsi.EntityFrameworkCore.Generator.Emit;

/// <summary>Writes indented C# with <c>\n</c> line endings, so output is the same on every platform.</summary>
internal sealed class CodeWriter
{
    private const string Indentation = "    ";

    private readonly StringBuilder _text = new();
    private int _depth;

    public CodeWriter Line(string line = "")
    {
        if (line.Length > 0)
        {
            for (var i = 0; i < _depth; i++)
            {
                _text.Append(Indentation);
            }

            _text.Append(line);
        }

        _text.Append('\n');
        return this;
    }

    /// <summary>Writes <paramref name="line"/>, then an opening brace, and indents until <see cref="Close"/>.</summary>
    public CodeWriter Open(string line)
    {
        Line(line);
        Line("{");
        _depth++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _depth--;
        return Line("}" + suffix);
    }

    public override string ToString() => _text.ToString();
}
