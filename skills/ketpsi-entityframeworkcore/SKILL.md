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
- `Select(x => ...)`, then `Skip`, `Take`, `Distinct`, aggregates and the terminals below, with a predicate when it
  reads the projection's entity and column members (see Projections below).
- `FromSql($"...")`, `FromSqlInterpolated($"...")` and `FromSqlRaw("...", args)` on a `DbSet`, with any operator after
  them, and `db.Database.SqlQuery<T>($"...")` and `SqlQueryRaw<T>` for scalars and unmapped types (see Raw SQL below).
- `Join(inner, outerKey, innerKey, (o, i) => ...)`, inner joins in method syntax (see Join below).
- `Distinct()` on the entities or on a projection.
- `GroupBy(x => key)`, with an element or result selector too, then `Where`, `OrderBy`/`ThenBy`, `Select` of keys and
  aggregates, `Count`, `LongCount`, `Any`, `ToList` and `ToArray` on the groups (see GroupBy below).
- Terminals: `ToList`, `ToArray`, `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Single`, `SingleOrDefault`,
  `Count`, `LongCount`, `Any`, their `Async` versions and predicate overloads, plus `foreach` / `await foreach` over the
  query.
- `db.Blogs.Find(id)`, `FindAsync(id)` and `FindAsync([id], ct)` work as in EF Core: an entity the context already
  tracks under the key (even added or deleted) is returned without a query; otherwise it is read with
  `SELECT TOP(1) ... WHERE [Id] = @p0`, with query filters and the context's default tracking (`Find` on a no-tracking
  context returns an untracked entity). Key values with a `null` return `null`; the wrong number or type of key values
  throws EF Core's `ArgumentException` when `Find` or `FindAsync` is called. Composite keys take their values in key
  order.
- `ExecuteDelete()` and `ExecuteDeleteAsync(ct)` delete the rows the query selects and return how many, as in EF Core:
  `DELETE FROM [t0] FROM [Posts] AS [t0] ... WHERE ...` with the query filters, joined navigations and included
  references. After `Skip` or `Take` the rows are the ones whose keys a subquery selects (`WHERE [Id] IN (SELECT ...)`),
  which a keyless entity doesn't have (`NotSupportedException`); an ordering without paging, `Distinct` and tracking
  don't change what is deleted. Nothing is loaded and the change tracker is left alone, so a tracked entity that was
  deleted stays `Unchanged`. Not after `Select` (KP1007) or `GroupBy` (KP1008).
- `Last` and `LastOrDefault` need an `OrderBy` before them, on the entities or before a `Select`, as in EF Core: they
  read the query in reverse order and take the first row (`TOP(1) ... ORDER BY ... DESC`). Without an ordering they
  throw EF Core's `InvalidOperationException` ("must have a deterministic sort order") when they run; an ordering before
  `Distinct` doesn't count, because distinct rows have no order.
- Aggregates: `Sum`, `Average`, `Min`, `Max` and their `Async` versions, with a selector that reads one property
  (`Sum(o => o.Total)`, `Max(p => p.Blog.Name)`) or after a `Select` of one property (`Select(o => o.Total).Sum()`).
  The SQL and the empty-set behavior are EF Core's: `Sum` of no rows is 0, a nullable or reference result is null, and
  `Min`, `Max` or `Average` of no rows into a non-nullable value type throws.

**Not supported yet** (build error): `GroupJoin`, `SelectMany`, `TagWith`, `ExecuteUpdate`, `AsAsyncEnumerable`.

Other `System.Linq.Enumerable` methods (`AsEnumerable`, `ToDictionary`, `ToHashSet`, ...) are rejected directly
on a `DbSet`, but after any KetPsi operator they run the query and finish in memory:
`db.Blogs.AsNoTracking().ToDictionary(b => b.Id)` works.

**Predicates** compare an entity property with a value:

- `==`, `!=`, `<`, `<=`, `>`, `>=` (value on either side), `&&`, `||`, `!`, a bool property, `== null`, `!= null`,
  `.HasValue`, `string.Equals(value)`, enums, nullable properties.
- Properties through reference navigations, in `Where`, `WhereIf` and the predicates of `First`, `Last`, `Single`,
  `Count`, `Any` and the rest: `p => p.Blog.Name == name`, `p => p.Blog.Owner.IsActive`. Each navigation is joined
  like an included reference: `INNER JOIN` when required, `LEFT JOIN` when optional, with the target's query filters
  in `ON`; a predicate and an ordering through the same navigation share one join. Through an optional navigation the
  value is null when there is no related entity, so `p.Blog.Name != name` also keeps posts without a blog, as in
  EF Core.
- `p.Blog == null` and `p.Blog != null` check the target's key, and a key of the target, such as `p.Blog.Id`, is read
  from the foreign key without a join (`[BlogId] IS NULL`), as in EF Core. When the target has query filters, or on
  the other side of a one-to-one, the target is joined and its key checked, so a filtered-out target counts as
  missing.
- `list.Contains(b.Property)` for arrays and lists (one JSON parameter, `OPENJSON` on SQL Server).
- Values may be constants, locals, parameters, fields/properties of them, or static members. Compute anything else
  into a local first (KP2007).
- Not translatable: comparing two properties (KP2006), comparing a navigation with anything but `null` (KP2002),
  method calls such as `ToUpper()` or `StartsWith` (KP2003), `EF.Functions` (KP2004), related collections such as
  `b.Posts.Any()` or `b.Posts.Count` (KP2005).
- Navigations are not supported yet inside query filters (KP3005) and inside the expressions on groups (KP2002), such
  as `g.Count(p => p.Blog.IsActive)`.
- Null semantics follow C#: `!(b.Views > 10)` keeps rows with null `Views`.

**Order keys** must be a property of the entity or of a reference navigation: `b => b.Name` or `p => p.Blog.Name`,
joined like a predicate's navigation; not `b => b.Name.Length` or `b => b.Posts.Count` (KP2008).

**Lambdas** must be written at the call site, not passed as variables or method groups (KP1005).

**Method syntax only.** Query syntax over a query (`from b in db.Blogs where b.IsActive select b`) is a build error
(KP1009): the compiler gives interceptors no call to replace in a query expression, so it would run through EF Core's
own pipeline. Write the same query with methods: `db.Blogs.Where(b => b.IsActive)`.

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
- `Where` and `OrderBy` through a navigation share the join with an included reference on the same path.

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
- Because computed members follow C#, a few results can differ from EF Core where EF Core computes in SQL:
  `Math.Round` rounds halves to even (SQL rounds them away from zero), string methods use the current culture, and
  `b.Author != null` reads the author entity, which a tracking query then tracks. Compare a foreign key instead
  (`b.AuthorId != null`), or round explicitly with `MidpointRounding.AwayFromZero`.
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
- After `Select`, `Skip`, `Take`, `Distinct`, aggregates and terminals are allowed.
  `Skip`/`Take` after `Select` page the entity rows, so they give the same rows as before it.
- `Where`, `OrderBy`/`ThenBy`, another `Select`, `Join` and the terminals' predicates (`Count(x => ...)`,
  `First(x => ...)`, ...) can follow a `Select` when they read only its members that are entities or columns as they
  are, before any paging: `Select(p => new { p, p.Blog }).Where(x => x.Blog.Rating > 1)`
  is `WHERE` on the blog's column. A member that is computed, or anything after `Skip`, `Take` or `Distinct`, is
  KP1007; filter and order before the `Select` instead.
- `Distinct` after `Select` compares the projected columns in SQL, so it can't follow computed members (KP1007).
  An aggregate after `Select` needs a projection of one property.
- `Count`, `LongCount` and `Any` ignore the projection, so they don't join its navigations, as in EF Core: with a
  required, filtered reference, `Count()` can be higher than `ToList().Count`.
- Not supported yet (KP2009): collection navigations (`b.Posts`, `b.Posts.Count`, `b.Posts.Any()`; include them on a
  projected entity instead), calling methods of the enclosing instance or projecting `this` (compute the value
  first), types or members the generated code cannot reach (private nested types and methods), and aggregate
  selectors that compute (`Sum(o => o.Price * o.Quantity)`).
- The indexed overload `Select((x, i) => ...)` is not supported (KP1006).

## Raw SQL

```csharp
var blogs = await db.Blogs
    .FromSql($"SELECT * FROM dbo.Blogs WHERE Rating >= {minimum}")   // each value becomes a parameter
    .Where(b => b.IsActive)
    .Include(b => b.Posts)
    .ToListAsync(ct);

var fromProcedure = db.Blogs.FromSqlRaw("EXEC dbo.GetBlogs {0}", minimum).AsNoTracking().ToList();
```

- The SQL is a runtime value, so nothing about it is checked at build time; the operators after it are translated as
  on any query.
- As in EF Core, SQL that nothing composes over runs exactly as written, and its columns are read by name, ignoring
  case and order; extra columns are ignored and a missing one throws "The required column 'X' was not present".
  Tracking operators and `IgnoreQueryFilters` don't compose. Anything else does, and so does a query filter on the
  entity: the SQL becomes a derived table, `SELECT ... FROM (your SQL) AS [t0] WHERE ...`.
- Only SQL that starts with `SELECT` (after whitespace and comments) can be composed over. A stored procedure call
  (`EXEC`), `WITH ...` or a leading `(` runs only as written; composing over it throws EF Core's "non-composable SQL"
  error. Call `AsEnumerable()` after an uncomposed `ToList()` to filter in memory instead. An entity with a query
  filter composes too, so call `IgnoreQueryFilters()` to run a stored procedure for it.
- Interpolated values and `{0}` placeholders become parameters `@p0`, `@p1`, ... in order, never concatenated into
  the SQL; a `DbParameter` argument is sent as it is, and the query's own parameters never take its name.
  `FromSqlRaw` without arguments sends braces as written.
- It composes with everything: `Where` (navigations too), `Include`, `Select`, `GroupBy`, `First`, `Count`,
  `ExecuteDelete` (by the keys the SQL selects) and `Join`, whose inner side can be `FromSql` too.

`db.Database.SqlQuery<T>($"...")` and `SqlQueryRaw<T>("...", args)` read scalars and types that aren't entities, with
the same rules for parameters, composing and non-composable SQL:

```csharp
var ids = db.Database.SqlQuery<int>($"SELECT Id AS Value FROM dbo.Blogs").Where(id => id > minimum).ToList();
var rows = db.Database.SqlQuery<BlogRow>($"EXEC dbo.BlogRows").ToList();   // public sealed record BlogRow(int Id, string Name);
```

- A scalar (`int`, `string`, `int?`, an enum, ...) is read from the first column when the SQL runs as written, and from
  the column named `Value` when something composes over it, as in EF Core; name the column `Value` to compose.
- A class or record is read from the columns named like its properties, ignoring case and order, through an object
  initializer or a constructor whose parameters are its properties. A missing column throws "The required column 'X'
  was not present". A null in a non-nullable column throws, as in EF Core: "Nullable object must have a value." for a
  scalar, the provider's error for a property; use `int?` or `string?` for columns that can be null.
- `Where`, `OrderBy`, `Select`, paging, `Distinct`, aggregates and the terminals, with or without a predicate, work
  on the result's members, such as `rows.Where(r => r.Id > 1).Select(r => r.Name)` or `ids.Count(v => v > 1)`.
  Results are never tracked.
- Not supported (KP1004): a type with navigation properties (EF Core rejects it too; use `FromSql` on its set), a
  collection, or a type without an accessible parameterless constructor or one whose parameters are its properties.

## Join

```csharp
var rows = await db.Posts
    .Join(db.Blogs.Where(b => b.IsActive), p => p.BlogId, b => b.Id, (p, b) => new { p, b })
    .Where(x => x.b.Rating > minimum)
    .OrderBy(x => x.b.Name).ThenByDescending(x => x.p.Title)
    .Select(x => new { x.p.Title, Blog = x.b.Name })
    .ToListAsync(ct);
```

- `INNER JOIN` on the keys, as EF Core: a property on each side, or anonymous types of them for composite keys
  (`p => new { A = p.BlogId, B = p.Lang }`). The outer key can read through reference navigations; the inner key is a
  property of the inner entity itself. `(int?)` casts on keys are fine.
- The inner sequence is a `DbSet` of the same context, optionally with `Where`, `WhereIf`, the tracking operators and
  `IgnoreQueryFilters`; its query filters go in `ON`, its predicates in `WHERE`. Another context throws EF Core's
  "Cannot use multiple context instances" error.
- The outer query can have any operators before the join; paging before it joins a derived table, as in EF Core.
- The result selector is a projection of both sides: columns, the entities themselves (tracked like any projected
  entity), computed members. A navigation of the inner entity (`b.Owner.Name`) is KP2009; join that entity too.
- After the join: `Where`, `OrderBy`, `Select` and further joins reading through the result (`x.b.Rating`, a second
  `Join(db.Posts, x => x.b.Id, o => o.BlogId, ...)`), then paging, `Distinct`, aggregates and terminals. `Count` keeps
  the join, so it counts the joined rows.
- Write joins in method syntax: query syntax (`from p in db.Posts join b in db.Blogs ...`) is KP1009.
- Not supported (KP2012): an inner sequence that is not a filtered `DbSet` (a list, or one with `OrderBy` or `Take`),
  inner keys through navigations, and computed keys. `GroupJoin` and `SelectMany` (left joins) are KP1001.

## GroupBy

```csharp
var stats = await db.Posts
    .Where(p => p.IsPublished)
    .GroupBy(p => new { p.BlogId, p.Blog.Name })
    .Where(g => g.Count() > 1)
    .Select(g => new { g.Key.Name, Posts = g.Count(), Likes = g.Sum(p => p.Likes), Recent = g.Count(p => p.Year >= year) })
    .OrderByDescending(x => x.Likes)
    .Take(10)
    .ToListAsync(ct);
```

- Keys: a property, one through reference navigations (`p => p.Blog.Name`, joined like an included reference), or an
  anonymous type of them. Computed, constant and entity keys are KP2010, and so are `ValueTuple.Create` keys, which EF
  Core cannot translate either.
- Group values: `g.Key` and its members, `g.Count()`, `g.LongCount()`, `g.Count(predicate)` and
  `g.Where(predicate).Count()` (`COUNT(CASE WHEN ...)`), `g.Sum/Average/Min/Max(x => x.Property)`, and
  `g.Select(x => x.Property).Distinct().Count()` (`COUNT(DISTINCT ...)`). With an element selector that reads one
  property, `g.Sum()`, `g.Max()` and the rest work without a selector. The SQL is EF Core's.
- A `Select` on the groups can compute in C# from those values (`g.Key + "!"`, `g.Count() * 2`), as projections do.
  EF Core 10 computes some of these in SQL instead: `g.Key + "!"` on an enum key gives `"0!"` there and `"Draft!"`
  in KetPsi, EF Core 8 and EF Core 9.
- `Where` on the groups, before or after the `Select`, becomes `HAVING`; `OrderBy`/`ThenBy` on keys and aggregates,
  before or after it, becomes `ORDER BY`. After the `Select` they read members that are a key part or an aggregate as
  they are. Then page, `Distinct`, aggregate (`Select(g => g.Count()).Max()`) and take the first, as after any `Select`.
- `Count()`, `LongCount()` and `Any(predicate)` of the groups count them through a derived table or `EXISTS`.
- An ordering before `GroupBy` is dropped, as in EF Core; `Skip`/`Take` before it group the page.
- `GroupBy(...).ToList()` and `foreach` return the groups themselves: the rows are read ordered by the key and grouped
  where the key changes, so the order inside a group is the database's, as in EF Core. Elements are tracked and
  identity-resolved like any queried entity, and get the includes before the `GroupBy`. After `Where` or `OrderBy` on
  the groups, they can only be selected from (KP1008). As in EF Core, a null in a nullable value-type key (`int?`)
  throws "Nullable object must have a value." when the groups are returned directly; a `Select` on the groups reads
  the null key.
- Not supported yet (KP2011): `g.First()`, `g.ToList()`, `g.Select(...).ToList()` and other uses of the group's rows,
  aggregates through navigations (`g.Max(p => p.Blog.Rating)`, a correlated subquery in EF Core), aggregate selectors
  that compute (`g.Sum(p => p.Likes * 2)`), and count predicates that compare with the key. Operators other than the
  ones above on the groups are KP1008, and `GroupBy` after `Select` is KP1007.

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
   tracking queries there, `Find` and `FromSql` on a tracking context included; use `AsNoTracking()` (or set the context's
   `QueryTrackingBehavior` to `NoTracking`), or move to EF Core 9+. `ExecuteDelete` never tracks, so it works on all
   three. No-tracking queries with `Include` work under Native AOT on EF Core 8, 9 and 10.
4. Keep `InvariantGlobalization` off; SqlClient needs culture data.
5. Expect EF Core's own IL2026/IL3050 warnings on the `DbContext` constructor; KetPsi itself adds none.
6. Decimal comparisons in predicates (`p.Price < 20m`) are safe: the compiler builds them by reflection, and the
   generated interceptor keeps decimal's operator methods for it. Nothing to configure.
7. A `Select` or `GroupBy` key to an anonymous type, or a `Select` to an object initializer, warns IL2026 in your code
   (`Expression.New` with members, `Expression.Bind`): the compiler builds the lambda's expression tree with
   reflection over the result type's properties. KetPsi never reads that tree, so the warning is harmless and the
   query runs under Native AOT. Records, other constructors, single members and `ValueTuple.Create` don't warn; use one
   of them where trim warnings must stay at zero.
8. `FromSql`, `FromSqlRaw`, `Database.SqlQuery<T>` and `SqlQueryRaw<T>` run under Native AOT on EF Core 8, 9 and 10:
   their readers are generated, and `SqlQuery<T>` needs no entity in the model. `Database.ExecuteSql` and
   `ExecuteSqlRaw` are EF Core's own (no LINQ to translate) and work under Native AOT too.

## Fixing KP diagnostics

| Id | Fix |
|----|-----|
| KP0002 | Keep `KetPsi.EntityFrameworkCore.Generated` in `InterceptorsNamespaces` |
| KP1001 | Rewrite without the operator, or load and finish in memory after `ToList()` |
| KP1002 | Include one navigation per lambda, chain with `ThenInclude`, filter collections in a separate query |
| KP1004 | Make the `SqlQuery<T>` result a scalar, or a class or record of column properties without navigations, constructible by generated code |
| KP1005 | Write the lambda inline at the call |
| KP1006 | Use the plain overload (no comparer, no index, no default value) |
| KP1007 | Filter and order before `Select`; after it use `Skip`, `Take`, `Distinct` (no computed members), aggregates of one property, and terminals, plus `Where`, `OrderBy`, `Select`, `Join` and terminals with a predicate that read its entity and column members before any paging (after a `Select` on groups, `Where` and `OrderBy` on keys and aggregates); group the entities, not a projection |
| KP1008 | On groups use `Where`, `OrderBy`/`ThenBy`, `Select`, `Count`, `LongCount`, `Any`, `ToList`, `ToArray`; select the key and aggregates to page or take the first |
| KP1009 | Rewrite the query expression with methods: `where` is `Where`, `orderby` is `OrderBy`/`ThenBy`, `select` is `Select` |
| KP2001–KP2005 | Simplify the predicate to comparisons of a property (or one through reference navigations) with a value; query related collections separately |
| KP2006 | Compare a property with a value, not with another property |
| KP2007 | Move the value into a local variable before the query |
| KP2008 | Order by a property of the entity or of a reference navigation |
| KP2009 | Avoid collection navigations and instance methods in projections; aggregate one property; make projected types accessible |
| KP2010 | Group by properties, ones through reference navigations, or an anonymous type of them |
| KP2011 | Read groups through `g.Key` and aggregates of one property of the grouped entity; query the rows separately |
| KP2012 | Join a filtered `DbSet` on properties: properties or navigations on the outer side, the inner entity's own properties on the inner side |
| KP3001–KP3004 | Change the mapping to a supported type and shape |
| KP3005 | Make the query filter a translatable predicate |
