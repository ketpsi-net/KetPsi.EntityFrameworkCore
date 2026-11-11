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

That is the whole API: you keep writing EF Core LINQ, in method syntax. The only addition is `WhereIf`. Query syntax
(`from b in db.Blogs where ... select ...`) is a build error (KP1009), because interceptors cannot replace the calls
the compiler makes for it.

> **Preview.** Filtering, ordering, paging, tracking, `Include`/`ThenInclude`, projection (`Select`), `Distinct`,
> aggregates, `GroupBy`, `Join` and the common terminals work today. Anything not supported yet is a
> **build error** with a `KP` diagnostic, never a silent fallback to EF Core's runtime pipeline.

## Install

```bash
dotnet add package KetPsi.EntityFrameworkCore.SqlServer --prerelease
```

The SQL Server package brings in `KetPsi.EntityFrameworkCore`, which contains the runtime, the source generator and the
MSBuild settings that enable its interceptors. Building needs the .NET 9 SDK (9.0.100) or later. It
targets `net9.0`, `net10.0` and `net11.0` with EF Core 9, 10 and 11; EF Core 8 is not supported.

## What is supported

| Area | Supported |
|------|-----------|
| Sources | `db.Blogs`, `db.Set<Blog>()`, and queries composed across statements (`IQueryable<Blog> q = db.Blogs; ...`) |
| Filtering | `Where`, `WhereIf(condition, predicate)`, on properties of the entity and of reference navigations (`p => p.Blog.Name == name`, `p => p.Blog != null`) |
| Ordering | `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending` on a property of the entity or of a reference navigation |
| Paging | `Skip`, `Take`, in any order and combination, as EF Core translates them |
| Tracking | `AsTracking()`, `AsTracking(behavior)`, `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()`, and the context's `QueryTrackingBehavior` |
| Query filters | Model query filters always apply; `IgnoreQueryFilters()` (and, on EF Core 10 and later, `IgnoreQueryFilters(["name"])`) |
| Related data | `Include(x => x.Navigation)`, `ThenInclude(x => x.Navigation)`; `AsSplitQuery()` and `AsSingleQuery()` are accepted |
| Projection | `Select` to anonymous types, records and constructors, object initializers, `ValueTuple.Create` or one member, reading properties, reference navigations and entities, with computed members; then `Skip`, `Take`, `Distinct`, aggregates and terminals |
| Distinct | `Distinct()` on entities and on projections without computed members |
| Aggregates | `Sum`, `Average`, `Min`, `Max` and their `Async` versions, over one property (through reference navigations too) or a projection of one property |
| Joins | `Join` of a filtered `DbSet` on properties or anonymous types of them, with a result selector; then `Where`, `OrderBy`, `Select` and further joins through its result, paging and terminals |
| Grouping | `GroupBy` on properties, ones through reference navigations, or an anonymous type of them, with an element or result selector too; then `Where`, `OrderBy`/`ThenBy`, a `Select` of keys and aggregates, `Count`, `LongCount`, `Any`, `ToList` and `ToArray` on the groups |
| Raw SQL | `FromSql`, `FromSqlInterpolated` and `FromSqlRaw` on a set, sent as written or composed over with any operator; `Database.SqlQuery<T>` and `SqlQueryRaw<T>` for scalars and unmapped classes and records |
| Bulk delete | `ExecuteDelete` and `ExecuteDeleteAsync` after the filtering, ordering and paging operators, without loading or tracking the rows |
| Lookup by key | `Find` and `FindAsync`, which return a tracked entity without a query, as EF Core does |
| Terminals | `ToList`, `ToArray`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault` (after an `OrderBy`, as in EF Core), `Single`, `SingleOrDefault`, `Count`, `LongCount`, `Any`, their `Async` versions, the predicate overloads, and `foreach` / `await foreach` |

### Predicates

- Comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`) between an entity property and a value, either way round.
- `&&`, `||`, `!`, boolean properties, `== null`, `!= null` and `.HasValue`.
- `string.Equals(value)`, enums, nullable properties.
- `list.Contains(b.Property)` over arrays and lists, sent as one JSON parameter through `OPENJSON`.
- Properties through reference navigations, such as `p.Blog.Name == name`, joined like an included reference
  (`INNER JOIN` when required, `LEFT JOIN` when optional), and `p.Blog == null`, which checks the foreign key.
- Values: constants, locals, parameters, fields and properties of them, static members. Every value is sent as a
  `DbParameter`; nothing is concatenated into SQL.

Null handling follows C#, as EF Core 9 and later do: `b.Views != views` with `views == null` means `IS NOT NULL`, and
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

Not supported yet: value converters, owned and complex types, JSON columns, inheritance, many-to-many navigations
without a join entity, collection navigations inside predicates (`b.Posts.Any()`), and navigations inside query
filters.

### Include

References are joined into the query (`INNER JOIN` when required, `LEFT JOIN` when optional); each included collection
is loaded with one more statement that matches the keys of the parents already read, so paging and filters on the
parent apply exactly. A collection `Include` appends the key to the ordering, as EF Core does. Tracking, no-tracking
and identity-resolution queries connect the entities the way EF Core does, and included collections are never null.

### Projection

`Select` reads only the columns the lambda uses. Its members can be entity properties, values through reference
navigations (`p.Blog.Owner.Name`, joined like an included reference) and entities (`p`, `p.Blog`). Projected entities
are tracked, identity-resolved and given the includes below them, as in EF Core. A value through a missing optional
navigation is null, and a non-nullable value type there throws as it does in EF Core unless it is cast to nullable.
Computed members run in C# on the values read, as EF Core evaluates a final projection on the client; captured values
are read each time the query runs. Following C# rather than SQL, `Math.Round` rounds halves to even and string methods
use the current culture. After `Select`, `Skip`, `Take`, `Distinct`, aggregates and terminals are allowed, and
`Where`, `OrderBy`, `Select`, `Join` and terminals with a predicate that read only its entity and column members, before
any paging.
Collection navigations in projections are not supported yet.

### Raw SQL

`FromSql`, `FromSqlInterpolated` and `FromSqlRaw` start a query from your SQL, as in EF Core: each value becomes a
parameter, SQL that nothing composes over runs as written (a stored procedure too) and is read by column name, and
any other operator, or a query filter, wraps it in a derived table. Composing over SQL that doesn't start with
`SELECT` throws EF Core's "non-composable SQL" error.

```csharp
var blogs = db.Blogs.FromSql($"SELECT * FROM dbo.Blogs WHERE Rating >= {minimum}").Where(b => b.IsActive).ToList();
var ids = db.Database.SqlQuery<int>($"SELECT Id AS Value FROM dbo.Blogs").Where(id => id > minimum).ToList();
```

`Database.SqlQuery<T>` reads a scalar from its first column, or from `Value` when composed over, and a class or record
from the columns named like its properties, as EF Core does. Its results are never tracked.

### Join

`Join` is an `INNER JOIN`, as in EF Core: the inner side is a `DbSet` with optional `Where`, its query filters go in
`ON`, and keys are properties or anonymous types of them. Write it in method syntax; the operators after it read
through the result selector's members:

```csharp
var rows = db.Posts
    .Join(db.Blogs, p => p.BlogId, b => b.Id, (p, b) => new { p, b })
    .Where(x => x.b.Rating > 2)
    .OrderBy(x => x.p.Title)
    .Select(x => new { x.p.Title, x.b.Name })
    .ToList();
```

### Aggregates

`Sum`, `Average`, `Min` and `Max` produce the SQL EF Core produces (`COALESCE(SUM(...), 0)`, `AVG(CAST(... AS float))`
for integers) and behave the same on no rows: `Sum` is 0, nullable and reference results are null, and a non-nullable
`Min`, `Max` or `Average` throws.

### GroupBy

`GroupBy` groups in SQL, as EF Core does: keys become `GROUP BY` columns, a key through a navigation is joined, and
a `Select` on the groups reads `g.Key`, its members and aggregates of one property of the grouped entity:
`Count`, `LongCount` (with a predicate too, or after `Where`), `Sum`, `Average`, `Min`, `Max` and
`Select(x => x.Property).Distinct().Count()`. Members computed from those run in C#, like other projections. `Where`
and `OrderBy` on the groups, before or after the `Select`, become `HAVING` and `ORDER BY`; paging, `Distinct`,
aggregates and terminals can follow the `Select`. `Count` and `Any` of the groups run on the database. Returning the
groups themselves (`ToList`, `foreach`) reads the rows ordered by the key and groups them in memory, as EF Core does,
with tracked elements. Not supported yet: the group's rows inside a `Select` (`g.First()`, `g.ToList()`), aggregates
through navigations, and computed or constant keys.

## Native AOT

KetPsi itself produces no trim or AOT warnings. EF Core still needs a few things under Native AOT:

- **A compiled model.** Generate it with `dotnet ef dbcontext optimize` (add `--nativeaot`) and
  register it with `options.UseModel(...)`.
- **No query filters under AOT.** EF Core compiled models cannot contain query filters. Query filters work under JIT.
- **Tracking needs `--nativeaot`.** With it, tracking queries, `Include`, `Find` and `SaveChanges` work under Native
  AOT.
- **No `InvariantGlobalization`.** SqlClient fails with it enabled.
- **Decimal operators are handled.** The compiler builds decimal comparisons such as `p.Price < 20m` by reflection,
  which trimming would break; queries that use them keep decimal's operator methods, so they work without relying on
  what else the app references.
- **Anonymous types and object initializers in `Select`, and anonymous `GroupBy` keys, warn IL2026** in the calling code, because the compiler builds
  their expression tree with reflection over the result type's properties. KetPsi never reads that tree, so the
  warning is harmless and the query runs under Native AOT. Records, other constructors, single members and
  `ValueTuple.Create` don't warn; use one of them where trim warnings must stay at zero.
- **Raw SQL works under Native AOT.** `FromSql`, `Database.SqlQuery<T>` and their `Raw` forms run with generated
  readers on every version; `Database.ExecuteSql` is EF Core's own and works too.
- The `DbContext` constructor itself carries EF Core's trim and AOT warnings, which an application cannot remove.

## Diagnostics

| Id | Meaning |
|----|---------|
| KP0001 | The generator failed on a call (please report it) |
| KP0002 | Interceptors are not enabled for `KetPsi.EntityFrameworkCore.Generated` |
| KP1001 | Query operator not supported |
| KP1002 | Include path not supported: filtered include, a chain in one lambda, or a string path |
| KP1004 | `SqlQuery<T>` result type not supported: navigations, a collection, or no constructor generated code can use |
| KP1005 | A lambda must be written at the call site |
| KP1006 | Operator overload not supported |
| KP1007 | Operator not supported after `Select` |
| KP1008 | Operator not supported on the groups of `GroupBy` |
| KP1009 | Query syntax over a query, which interceptors cannot replace; use method syntax |
| KP2001–KP2008 | Expression, navigation, method, `EF.Functions`, collection, property comparison, value or order key not translatable |
| KP2009 | Projection not supported: a collection navigation, an instance method or `this`, a computed aggregate selector, or a type the generated code cannot reach |
| KP2010 | `GroupBy` key not supported: a computed, constant, entity or `ValueTuple.Create` key |
| KP2011 | Group expression not supported: the group's rows (`g.First()`, `g.ToList()`), an aggregate through a navigation or with a computed selector |
| KP2012 | Join not supported: an inner sequence that is not a filtered `DbSet`, an inner key through a navigation, or a computed key |
| KP3001–KP3005 | Property type, value converter, owned/complex/JSON mapping, entity construction or query filter not supported |

## How it works

1. The generator finds the entity types of every `DbContext` and every query call site on them.
2. Each operator is translated at build time into a template with value slots, and the call is replaced by an
   interceptor that records it in an immutable operator chain.
3. The terminal binds the chain to the EF Core model (table, columns, keys, query filters), builds parameterized SQL
   through the provider's dialect and reads rows with a generated reader.
4. Commands run on the DbContext's connection and join its current transaction.
