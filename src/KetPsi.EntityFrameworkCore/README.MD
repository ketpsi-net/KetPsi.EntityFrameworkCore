# KetPsi.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/KetPsi.EntityFrameworkCore.svg)](https://www.nuget.org/packages/KetPsi.EntityFrameworkCore/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

**Plain EF Core LINQ that runs under Native AOT.**

A Roslyn source generator finds every EF Core query in your code, translates it to SQL at build time and intercepts
the call. At runtime the query runs on the DbContext's own connection with a generated reader: no expression trees,
no runtime query compilation, nothing that trimming or Native AOT can break.

```csharp
var page = await db.Blogs
    .Where(b => b.IsActive && b.Rating >= minimum)
    .OrderBy(b => b.Name)
    .Skip(20).Take(10)
    .ToListAsync(ct);
```

That is the whole API: you keep writing EF Core LINQ. The only addition is `WhereIf`.

> **Preview.** Single-table queries, filtering, ordering, paging, tracking and the common terminals work today.
> Projection (`Select`), aggregates, `Include` and `GroupBy` are on the roadmap. Anything not supported yet is a
> **build error** with a `KP` diagnostic, never a silent fallback to EF Core's runtime pipeline.

## Install

```bash
dotnet add package KetPsi.EntityFrameworkCore.SqlServer --prerelease
```

The SQL Server package brings in `KetPsi.EntityFrameworkCore`, which contains the runtime, the source generator and the
MSBuild settings that enable its interceptors. Building needs the .NET 9 SDK (9.0.100) or later, even when targeting
`net8.0`.

## What is supported

| Area | Supported |
|------|-----------|
| Sources | `db.Blogs`, `db.Set<Blog>()`, and queries composed across statements (`IQueryable<Blog> q = db.Blogs; ...`) |
| Filtering | `Where`, `WhereIf(condition, predicate)` |
| Ordering | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` on a property of the entity |
| Paging | `Skip`, `Take`, in any order and combination, as EF Core translates them |
| Tracking | `AsTracking()`, `AsTracking(behavior)`, `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()`, and the context's `QueryTrackingBehavior` |
| Query filters | Model query filters always apply; `IgnoreQueryFilters()` (and, on EF Core 10, `IgnoreQueryFilters(["name"])`) |
| Terminals | `ToList`, `ToArray`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Count`, `LongCount`, `Any`, their `Async` versions, the predicate overloads, and `foreach` / `await foreach` |

### Predicates

- Comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`) between an entity property and a value, either way round.
- `&&`, `||`, `!`, boolean properties, `== null`, `!= null` and `.HasValue`.
- `string.Equals(value)`, enums, nullable properties.
- `list.Contains(b.Property)` over arrays and lists, sent as one JSON parameter through `OPENJSON`.
- Values: constants, locals, parameters, fields and properties of them, static members. Every value is sent as a
  `DbParameter`; nothing is concatenated into SQL.

Null handling follows C#, as EF Core 9 and 10 do: `b.Views != views` with `views == null` means `IS NOT NULL`, and
`!(b.Views > 10)` keeps rows where `Views` is null.

### Tracking

Tracking queries behave like EF Core's: rows are attached to the change tracker as `Unchanged` with their shadow
values, an entity already tracked under the same key is returned as it is (not refreshed), and `SaveChanges` works as
usual. Keyless entities are never tracked. `AsNoTrackingWithIdentityResolution` returns one instance per key within a
query and tracks nothing.

### Supported property types

`bool`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`, `string`, `DateTime`, `DateTimeOffset`,
`DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `byte[]`, enums, and nullable versions of all of them, on classes with an
accessible parameterless constructor. Private setters and constructors are fine. Shadow properties are supported.

Not supported yet: value converters, owned and complex types, JSON columns, inheritance, and navigations in queries.

## Native AOT

KetPsi itself produces no trim or AOT warnings. EF Core still needs a few things under Native AOT:

- **A compiled model.** Generate it with `dotnet ef dbcontext optimize` (add `--nativeaot` on EF Core 9 and 10) and
  register it with `options.UseModel(...)`.
- **No query filters under AOT.** EF Core compiled models cannot contain query filters. Query filters work under JIT.
- **EF Core 8 cannot track or save under Native AOT.** KetPsi rejects tracking queries there with a clear error; use
  `AsNoTracking()` or move to EF Core 9 or later.
- **No `InvariantGlobalization`.** SqlClient fails with it enabled.
- The `DbContext` constructor itself carries EF Core's trim and AOT warnings, which an application cannot remove.

## Diagnostics

| Id | Meaning |
|----|---------|
| KP0001 | The generator failed on a call (please report it) |
| KP0002 | Interceptors are not enabled for `KetPsi.EntityFrameworkCore.Generated` |
| KP1001 | Query operator not supported |
| KP1002 | `Include` / `ThenInclude` not supported yet |
| KP1004 | Raw SQL not supported |
| KP1005 | A lambda must be written at the call site |
| KP1006 | Operator overload not supported |
| KP2001–KP2008 | Expression, navigation, method, `EF.Functions`, collection, property comparison, value or order key not translatable |
| KP3001–KP3005 | Property type, value converter, owned/complex/JSON mapping, entity construction or query filter not supported |

## How it works

1. The generator finds the entity types of every `DbContext` and every query call site on them.
2. Each operator is translated at build time into a template with value slots, and the call is replaced by an
   interceptor that records it in an immutable operator chain.
3. The terminal binds the chain to the EF Core model (table, columns, keys, query filters), builds parameterized SQL
   through the provider's dialect and reads rows with a generated reader.
4. Commands run on the DbContext's connection and join its current transaction.
