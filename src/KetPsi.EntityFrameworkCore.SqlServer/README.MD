# KetPsi.EntityFrameworkCore.SqlServer

[![NuGet](https://img.shields.io/nuget/v/KetPsi.EntityFrameworkCore.SqlServer.svg)](https://www.nuget.org/packages/KetPsi.EntityFrameworkCore.SqlServer/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

The SQL Server dialect for [KetPsi.EntityFrameworkCore](https://www.nuget.org/packages/KetPsi.EntityFrameworkCore/):
EF Core LINQ queries translated at build time and run on SQL Server, Native AOT friendly.

```bash
dotnet add package KetPsi.EntityFrameworkCore.SqlServer --prerelease
```

This is the only package an application needs. It depends on `KetPsi.EntityFrameworkCore`, which brings the runtime,
the source generator and its MSBuild settings. The dialect registers itself when the assembly loads; there is no
setup code. Keep configuring the context with `UseSqlServer` as usual.

```csharp
public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

var cheap = await db.Products
    .Where(p => p.IsActive && p.Price < 20m)
    .OrderBy(p => p.Price)
    .Take(10)
    .ToListAsync(ct);
```

## SQL Server specifics

- Values are typed `SqlParameter`s with the column's type, size, precision and scale, so `datetime2`, `decimal` and
  string columns keep their exact semantics.
- `list.Contains(x.Property)` sends the list as one JSON parameter and reads it with `OPENJSON`, so the statement text
  does not change with the list length. This needs SQL Server 2016 or later (compatibility level 130).
- Paging uses `OFFSET ... FETCH`; `Take` without ordering uses `TOP`.

See the [KetPsi.EntityFrameworkCore README](https://github.com/ketpsi-net/KetPsi.EntityFrameworkCore) for what is
supported, the diagnostics and the Native AOT notes.
