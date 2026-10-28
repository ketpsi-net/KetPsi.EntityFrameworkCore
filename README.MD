# KetPsi.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/KetPsi.EntityFrameworkCore.svg)](https://www.nuget.org/packages/KetPsi.EntityFrameworkCore/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

**Plain EF Core LINQ that runs under Native AOT.**

EF Core builds and compiles expression trees at runtime, which trimming and Native AOT do not allow. KetPsi moves that
work to build time: a source generator translates each LINQ query in your code to parameterized SQL and intercepts
the call, so at runtime the query runs on the DbContext's connection with a generated reader.

```csharp
var blogs = await db.Blogs
    .Where(b => b.IsActive && b.Rating >= minimum)
    .OrderBy(b => b.Name)
    .Take(10)
    .ToListAsync(ct);
```

Your code stays EF Core LINQ. What KetPsi cannot translate is a build error with a `KP` diagnostic; nothing falls back
to EF Core's runtime pipeline.

---

## Packages

| Package | Description |
|---------|-------------|
| **[KetPsi.EntityFrameworkCore.SqlServer](./src/KetPsi.EntityFrameworkCore.SqlServer)** | The SQL Server dialect. The one package an application installs. |
| **[KetPsi.EntityFrameworkCore](./src/KetPsi.EntityFrameworkCore)** | Runtime, source generator and MSBuild settings. Comes with the dialect package. |

```bash
dotnet add package KetPsi.EntityFrameworkCore.SqlServer --prerelease
```

Targets `net8.0`, `net9.0` and `net10.0`, each with its own EF Core major version.

---

## Design goals

- **No new API.** Queries are ordinary EF Core LINQ; `WhereIf` is the only addition.
- **Fail at build time.** Unsupported queries are compile errors, not runtime surprises.
- **EF Core semantics.** Results, exceptions, tracking and null handling match EF Core; a differential test suite runs
  every query shape through both against SQL Server.
- **Safe SQL.** Every value is a typed `DbParameter`.
- **Dialects behind an interface.** SQL Server is the first one.

## Status

Preview. Done: filtering, ordering, paging, terminals, query filters, tracking, identity resolution, and
`Include` / `ThenInclude`.
Next: projection (`Select`), aggregates and `Distinct`; then `GroupBy`.

See the [package README](./src/KetPsi.EntityFrameworkCore/README.MD) for the full support list, diagnostics and
Native AOT notes, and [skills/ketpsi-entityframeworkcore/SKILL.md](./skills/ketpsi-entityframeworkcore/SKILL.md) for a
usage guide written for coding agents.

## Building

```bash
dotnet build
dotnet test
```

The differential tests start SQL Server in Docker through Testcontainers. `eng/Update-CompiledModels.ps1` regenerates
the AOT sample's compiled models, and `eng/Test-AotWarnings.ps1` checks a Native AOT publish against the warning
baselines in `eng/aot-warnings`.

## License

[MIT](LICENSE)
