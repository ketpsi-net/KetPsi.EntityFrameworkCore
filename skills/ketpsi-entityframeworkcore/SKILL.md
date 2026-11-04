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
- `Include(x => x.Navigation)`, `ThenInclude(x => x.Navigation)`, `AsSplitQuery()`, `AsSingleQuery()` (see Include below).
- `Select(x => ...)`, then `Skip`, `Take`, `Distinct`, aggregates and the terminals below without a predicate (see
  Projections below).
- `Distinct()` on the entities or on a projection.
- Terminals: `ToList`, `ToArray`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Count`, `LongCount`, `Any`,
  their `Async` versions and predicate overloads, plus `foreach` / `await foreach` over the query.
- Aggregates: `Sum`, `Average`, `Min`, `Max` and their `Async` versions, with a selector that reads one property
  (`Sum(o => o.Total)`, `Max(p => p.Blog.Name)`) or after a `Select` of one property (`Select(o => o.Total).Sum()`).
  The SQL and the empty-set behavior are EF Core's: `Sum` of no rows is 0, a nullable or reference result is null, and
  `Min`, `Max` or `Average` of no rows into a non-nullable value type throws.

**Not supported yet** (build error): `GroupBy`, `Join`, `Last`,
`FromSql*` (KP1004), `TagWith`, `Find`, `ExecuteUpdate`/`ExecuteDelete`, `AsAsyncEnumerable`.

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

## Include

```csharp
var blogs = await db.Blogs
    .Include(b => b.Owner)                          // reference: joined into the same statement
    .Include(b => b.Posts).ThenInclude(p => p.Tags) // collections: one statement each, by the loaded parents' keys
    .Where(b => b.IsActive)
    .ToListAsync(ct);
```

- Each lambda reads exactly one navigation of its parameter. Continue a path with `ThenInclude`; repeated paths are
  included once.
- References are joined: `INNER JOIN` for a required navigation, `LEFT JOIN` for an optional one. A required
  reference whose target is removed by a query filter drops the row, as in EF Core.
- Collections load with their own statement, using the keys of the parents already read (one JSON parameter). Paging,
  filters and `First`/`Single` on the parent therefore apply exactly. Children come back in key order.
- A collection `Include` appends the entity key to the ordering, as EF Core does, so row order can differ from the
  same query without it.
- `AsSplitQuery()` and `AsSingleQuery()` are accepted and change nothing: KetPsi always loads collections with
  separate statements, which gives the same results.
- Tracking queries attach every entity and mark included navigations as loaded; no-tracking queries set navigations
  in both directions, with a separate instance of a shared reference per row; `AsNoTrackingWithIdentityResolution`
  shares one instance per key and, like the change tracker, connects any two entities the query read that are
  related, even through a navigation that wasn't included.
- Included collections are never null: a property without an initializer gets a `HashSet<T>` (interfaces that can
  hold one) or a `List<T>`, like EF Core. A null getter-only collection throws at runtime; initialize it.
- As in EF Core, a no-tracking query cannot include a navigation and then its inverse
  (`Include(p => p.Blog).ThenInclude(b => b.Posts)` with `AsNoTracking()`): it throws "results in a cycle". Use
  tracking or `AsNoTrackingWithIdentityResolution()`.
- `Count`, `LongCount` and `Any` ignore includes.
- Not supported (KP1002): filtered includes (`Include(b => b.Posts.Where(...))`), chains in one lambda
  (`Include(p => p.Blog.Owner)`, use `ThenInclude`), and string paths (`Include("Posts")`). Many-to-many navigations
  without a join entity class throw at runtime; map the join entity and include it with `ThenInclude`.
- Navigations inside `Where` are still not supported (KP2002).

## Projections

```csharp
var rows = await db.Posts
    .Where(p => p.IsPublished)
    .OrderBy(p => p.Title)
    .Select(p => new { p.Id, p.Title, BlogName = p.Blog.Name, p.Blog.Owner })
    .Take(20)
    .ToListAsync(ct);
```

- The statement reads only the columns the lambda uses. Results can be anonymous types (nested too), records and
  other constructors, object initializers, `ValueTuple.Create(...)`, or a single member (`p => p.Title`).
- Computed members run in C# on the values read, as EF Core evaluates a final projection on the client:
  `p.Title + "!"`, `p.Title.ToUpper()`, `Math.Round(p.Price, 2)`, `p.Created.Year`, `p.Views ?? 0`, conditionals,
  interpolated strings, your own static and extension methods. Captured variables, fields of the enclosing instance
  and static members are read when the query runs, so each run sees their current values. A `string` method on a null
  column gives null, as the SQL EF Core sends does; any other member of a null value throws, as it does in C#.
- Members read entity properties, follow reference navigations (`p.Blog.Owner.Name`), or are entities themselves
  (`p`, `p.Blog`). Each navigation is joined like an included reference: `INNER JOIN` when required, `LEFT JOIN`
  when optional, with the target's query filters in the join.
- A value read through an optional navigation is null when there is no related entity. For a non-nullable value
  type that throws "Nullable object must have a value.", as in EF Core; cast it, `(int?)p.Blog.Owner.Id`, to get null.
- Projected entities are tracked, identity-resolved and get the includes below them, as in EF Core:
  `Include(p => p.Blog).ThenInclude(b => b.Posts).Select(p => p.Blog)` loads each blog's posts. Includes of
  entities the projection doesn't select are ignored. Without tracking or identity resolution, every projected
  entity is its own instance and nothing is connected.
- A projection with entities reads every row before returning any; one with only columns streams them.
- After `Select`, only `Skip`, `Take`, `Distinct`, aggregates and terminals without a predicate are allowed (KP1007).
  Filter and order the entities before `Select`. `Skip`/`Take` after `Select` page the entity rows, so they give the
  same rows as before it.
- `Distinct` after `Select` compares the projected columns in SQL, so it can't follow computed members (KP1007).
  An aggregate after `Select` needs a projection of one property.
- `Count`, `LongCount` and `Any` ignore the projection, so they don't join its navigations, as in EF Core: with a
  required, filtered reference, `Count()` can be higher than `ToList().Count`.
- Not supported yet (KP2009): collection navigations (`b.Posts`, `b.Posts.Count`, `b.Posts.Any()`; include them on a
  projected entity instead), calling methods of the enclosing instance or projecting `this` (compute the value
  first), types or members the generated code cannot reach (private nested types and methods), and aggregate
  selectors that compute (`Sum(o => o.Price * o.Quantity)`).
- The indexed overload `Select((x, i) => ...)` is not supported (KP1006).

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
   move to EF Core 9+. No-tracking queries with `Include` work under Native AOT on EF Core 8, 9 and 10.
4. Keep `InvariantGlobalization` off; SqlClient needs culture data.
5. Expect EF Core's own IL2026/IL3050 warnings on the `DbContext` constructor; KetPsi itself adds none.
6. Decimal comparisons in predicates (`p.Price < 20m`) are safe: the compiler builds them by reflection, and the
   generated interceptor keeps decimal's operator methods for it. Nothing to configure.
7. A `Select` to an anonymous type or an object initializer warns IL2026 in your code: the compiler builds the
   lambda's expression tree with reflection over the result type's properties. KetPsi never reads that tree, so the
   warning is harmless. Records, other constructors, single members and `ValueTuple.Create` don't warn.

## Fixing KP diagnostics

| Id | Fix |
|----|-----|
| KP0002 | Keep `KetPsi.EntityFrameworkCore.Generated` in `InterceptorsNamespaces` |
| KP1001 | Rewrite without the operator, or load and finish in memory after `ToList()` |
| KP1002 | Include one navigation per lambda, chain with `ThenInclude`, filter collections in a separate query |
| KP1005 | Write the lambda inline at the call |
| KP1006 | Use the plain overload (no comparer, no index, no default value) |
| KP1007 | Filter and order before `Select`; after it use `Skip`, `Take`, `Distinct` (no computed members), aggregates of one property, and terminals without a predicate |
| KP2001–KP2005 | Simplify the predicate to property-vs-value comparisons |
| KP2006 | Compare a property with a value, not with another property |
| KP2007 | Move the value into a local variable before the query |
| KP2008 | Order by a direct property |
| KP2009 | Avoid collection navigations and instance methods in projections; aggregate one property; make projected types accessible |
| KP3001–KP3004 | Change the mapping to a supported type and shape |
| KP3005 | Make the query filter a translatable predicate |
