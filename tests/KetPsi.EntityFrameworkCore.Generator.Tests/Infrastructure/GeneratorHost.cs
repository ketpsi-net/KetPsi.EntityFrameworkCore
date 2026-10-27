using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

/// <summary>Compiles test sources against .NET, EF Core and KetPsi, and runs the generator on them.</summary>
internal static class GeneratorHost
{
    public const string TestFilePath = "Queries.cs";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    // Every assembly the test process can load: the .NET runtime, EF Core and the SQL Server provider, KetPsi.
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(static () =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path)),
        ]);

    /// <summary>The KetPsi SQL Server dialect assembly, which tests can leave out to check what depends on it.</summary>
    public const string SqlServerDialectAssembly = "KetPsi.EntityFrameworkCore.SqlServer.dll";

    public static CSharpCompilation Compile(string source, params MetadataReference[] extraReferences)
        => Compile("TestAssembly", source, extraReferences);

    public static CSharpCompilation Compile(string assemblyName, string source, params MetadataReference[] extraReferences)
        => Compile(assemblyName, source, excludedAssembly: null, extraReferences);

    public static CSharpCompilation Compile(string assemblyName, string source, string? excludedAssembly, params MetadataReference[] extraReferences)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, TestFilePath);
        var references = References.Value.Where(r => excludedAssembly is null
            || !string.Equals(Path.GetFileName(((PortableExecutableReference)r).FilePath), excludedAssembly, StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            [.. references, .. extraReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, "The test source must compile: " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        return compilation;
    }

    /// <summary>Compiles <paramref name="source"/> into an assembly that other test compilations can reference.</summary>
    public static MetadataReference CompileReference(string assemblyName, string source)
    {
        using var stream = new MemoryStream();
        var result = Compile(assemblyName, source).Emit(stream);
        Assert.True(result.Success);
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    public static GeneratorRun Run(string source, bool interceptorsEnabled = true, params MetadataReference[] extraReferences)
        => Run(Compile(source, extraReferences), interceptorsEnabled);

    public static GeneratorRun Run(Compilation compilation, bool interceptorsEnabled = true)
        => Run(compilation, interceptorsEnabled
            ? new Dictionary<string, string> { [QueryInterceptorGenerator.InterceptorsNamespacesProperty] = QueryInterceptorGenerator.GeneratedNamespace }
            : []);

    public static GeneratorRun Run(Compilation compilation, IReadOnlyDictionary<string, string> globalOptions)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new QueryInterceptorGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(globalOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratorRun(compilation, driver.GetRunResult(), diagnostics, output);
    }
}

internal sealed record GeneratorRun(
    Compilation Input,
    GeneratorDriverRunResult Result,
    ImmutableArray<Diagnostic> Diagnostics,
    Compilation Output)
{
    /// <summary>The text of the one generated file with <paramref name="hintName"/>.</summary>
    public string Generated(string hintName)
        => Assert.Single(Result.Results.Single().GeneratedSources, s => s.HintName == hintName).SourceText.ToString();

    /// <summary>Compiler errors and warnings in the output compilation, generated code included.</summary>
    public IReadOnlyList<Diagnostic> OutputProblems()
        => [.. Output.GetDiagnostics().Where(static d => d.Severity >= DiagnosticSeverity.Warning)];

    /// <summary>Emits the output compilation and loads it, running its module initializers.</summary>
    public System.Reflection.Assembly Load()
    {
        using var stream = new MemoryStream();
        var result = Output.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }

    /// <summary>The source text a diagnostic points at.</summary>
    /// <remarks>
    /// The generator keeps locations as plain data, so its diagnostics carry a file path and span rather than
    /// a syntax tree; the text is read from the input tree with that path.
    /// </remarks>
    public string TextAt(Diagnostic diagnostic)
    {
        var path = diagnostic.Location.GetLineSpan().Path;
        var tree = Input.SyntaxTrees.FirstOrDefault(t => t.FilePath == path);
        return tree is null ? "" : tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}
