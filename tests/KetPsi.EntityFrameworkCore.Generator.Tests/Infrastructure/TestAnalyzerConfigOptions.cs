using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

/// <summary>Global analyzer options, the way MSBuild passes <c>CompilerVisibleProperty</c> values.</summary>
internal sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
}

internal sealed class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalValues) : AnalyzerConfigOptionsProvider
{
    private static readonly TestAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(globalValues);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
}
