namespace KetPsi.EntityFrameworkCore.Generator.Model;

/// <summary>Which KetPsi packages the compilation references, which decides what generated code can call.</summary>
/// <param name="HasRuntime">Whether KetPsi.EntityFrameworkCore is referenced; without it nothing is generated.</param>
/// <param name="HasSqlServer">Whether KetPsi.EntityFrameworkCore.SqlServer is referenced, so its dialect is registered.</param>
internal sealed record GenerationReferences(bool HasRuntime, bool HasSqlServer);
