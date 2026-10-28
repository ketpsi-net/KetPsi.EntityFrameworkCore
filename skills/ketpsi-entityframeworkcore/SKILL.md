---
name: ketpsi-entityframeworkcore
description: How to write EF Core queries that KetPsi.EntityFrameworkCore translates at build time so they run under Native AOT. Use when adding or changing LINQ queries on a DbContext in a project that references KetPsi.EntityFrameworkCore or KetPsi.EntityFrameworkCore.SqlServer, when fixing KP0xxx/KP1xxx/KP2xxx/KP3xxx build errors, using WhereIf, tracking or query filters, or preparing an EF Core app for PublishAot.
---

# KetPsi.EntityFrameworkCore

A source generator intercepts every EF Core LINQ query call site, translates it to parameterized SQL at build time and
runs it on the DbContext's own connection with a generated reader. You write **ordinary EF Core LINQ**. Whatever the
generator cannot translate is a **build error** (`KP` diagnostic); there is no runtime fallback to EF Core's pipeline.

## Setup

```bash
dotnet add package KetPsi.EntityFrameworkCore.SqlServer --prerelease
```

- That package depends on `KetPsi.EntityFrameworkCore` (runtime + generator + MSBuild props/targets). Nothing else to
  register: the dialect and the generated readers register themselves at module load.
- Configure the context as usual (`UseSqlServer(...)`).
- The build needs the .NET 9 SDK (9.0.100) or later, also for `net8.0` targets.
- If the build reports **KP0002**, the project overrides `InterceptorsNamespaces` without keeping
  `KetPsi.EntityFrameworkCore.Generated`; append it instead of replacing the list.

## Writing queries

```csharp
var page = await db.Blogs
    .Where(b => b.IsActive && b.Rating >= minimum)
    .WhereIf(name != null, b => b.Name == name)   // KetPsi addition: applies the predicate only when true
    .OrderBy(b => b.Name).ThenByDescending(b => b.Id)
    .Skip(skip).Take(take)
    .ToListAsync(ct);
```

**Supported operators**

- Sources: `db.Blogs`, `db.Set<Blog>()`, and queries composed across statements (`IQueryable<Blog> q = db.Blogs;`
  then `q = q.Where(...)`).
- `Where`, `WhereIf`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, `Take`.
- `AsTracking()`, `AsTracking(behavior)`, `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()`,
  `IgnoreQueryFilters()` (EF Core 10 also `IgnoreQueryFilters(["name"])`).
- Terminals: `ToList`, `ToArray`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Count`, `LongCount`, `Any`,
  their `Async` versions and predicate overloads, plus `foreach` / `await foreach` over the query.

**Not supported yet** (build error): `Select`, `Distinct`, `GroupBy`, `Join`, `Include`/`ThenInclude` (KP1002),
`Sum`/`Min`/`Max`/`Average`, `Last`, `FromSql*` (KP1004), `TagWith`, `AsSplitQuery`, `Find`, `ExecuteUpdate`/`ExecuteDelete`,
`AsAsyncEnumerable`.

Other `System.Linq.Enumerable` methods (`AsEnumerable`, `ToDictionary`, `ToHashSet`, ...) are rejected directly
on a `DbSet`, but after any KetPsi operator they run the query and finish in memory:
`db.Blogs.AsNoTracking().ToDictionary(b => b.Id)` works.

**Predicates** compare an entity property with a value:

- `==`, `!=`, `<`, `<=`, `>`, `>=` (value on either side), `&&`, `||`, `!`, a bool property, `== null`, `!= null`,
  `.HasValue`, `string.Equals(value)`, enums, nullable properties.
- `list.Contains(b.Property)` for arrays and lists (one JSON parameter, `OPENJSON` on SQL Server).
- Values may be constants, locals, parameters, fields/properties of them, or static members. Compute anything else
  into a local first (KP2007).
- Not translatable: comparing two properties (KP2006), navigations (KP2002), method calls such as `ToUpper()` or
  `StartsWith` (KP2003), `EF.Functions` (KP2004), related collections (KP2005).
- Null semantics follow C#: `!(b.Views > 10)` keeps rows with null `Views`.

**Order keys** must be a property of the entity itself: `b => b.Name`, not `b => b.Name.Length` (KP2008).

**Lambdas** must be written at the call site, not passed as variables or method groups (KP1005).

## Tracking

Same behavior as EF Core:

- Default tracking queries attach rows as `Unchanged`, with shadow property values, so `SaveChanges` works after
  load-modify.
- A second query returns the instance already tracked for that key, **not refreshed** with database values.
- `AsNoTrackingWithIdentityResolution()` returns one instance per key within a query and tracks nothing.
- Keyless entities are never tracked.
- Scalar terminals (`Count`, `Any`) never track.

## Entities and model

- Supported property types: `bool`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`, `string`,
  `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `byte[]`, enums, and their nullable forms.
- Entities need a non-abstract class with an accessible parameterless constructor (private setters/constructors are
  fine, KP3004). Shadow properties work.
- Not supported yet: value converters (KP3002), owned/complex types and JSON columns (KP3003), inheritance, entities
  not mapped to a table (views count as unsupported).
- Query filters (`HasQueryFilter`) are translated at build time with the same predicate rules (KP3005 otherwise) and
  always apply unless `IgnoreQueryFilters()` is used. Values read from the context (for example a tenant id field)
  are fine.

## Native AOT checklist

1. Generate a compiled model: `dotnet ef dbcontext optimize --output-dir CompiledModels --namespace MyApp.CompiledModel`,
   adding `--nativeaot` on EF Core 9 and 10, and call `options.UseModel(MyApp.CompiledModel.MyDbModel.Instance)`.
2. Compiled models cannot hold query filters; filters only work under JIT.
3. EF Core 8 cannot track or save changes under Native AOT. KetPsi throws a clear `InvalidOperationException` for
   tracking queries there; use `AsNoTracking()` (or set the context's `QueryTrackingBehavior` to `NoTracking`), or
   move to EF Core 9+.
4. Keep `InvariantGlobalization` off; SqlClient needs culture data.
5. Expect EF Core's own IL2026/IL3050 warnings on the `DbContext` constructor; KetPsi itself adds none.

## Fixing KP diagnostics

| Id | Fix |
|----|-----|
| KP0002 | Keep `KetPsi.EntityFrameworkCore.Generated` in `InterceptorsNamespaces` |
| KP1001 | Rewrite without the operator, or load and finish in memory after `ToList()` |
| KP1002 | Load related entities with a separate query |
| KP1005 | Write the lambda inline at the call |
| KP1006 | Use the plain overload (no comparer, no index, no default value) |
| KP2001–KP2005 | Simplify the predicate to property-vs-value comparisons |
| KP2006 | Compare a property with a value, not with another property |
| KP2007 | Move the value into a local variable before the query |
| KP2008 | Order by a direct property |
| KP3001–KP3004 | Change the mapping to a supported type and shape |
| KP3005 | Make the query filter a translatable predicate |
