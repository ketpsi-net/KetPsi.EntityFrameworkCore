using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore;
using AotSample;
using Microsoft.EntityFrameworkCore;

const string ConnectionVariable = "KETPSI_SAMPLE_CONNECTION";
const int Success = 0;
const int Failure = 1;
const int Misconfigured = 2;

var serverConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
if (string.IsNullOrEmpty(serverConnectionString))
{
    Console.Error.WriteLine($"Set {ConnectionVariable} to a SQL Server connection string.");
    return Misconfigured;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var cancellationToken = cancellation.Token;

var connectionString = await SampleDatabase.ResetAsync(serverConnectionString, cancellationToken);
var options = ShopDb.Options(connectionString);

Console.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, Native AOT: {!RuntimeFeature.IsDynamicCodeSupported}");

var failures = 0;

async Task CheckAsync<T>(string name, Func<ShopDb, Task<T>> query, T expected)
{
    await using var db = new ShopDb(options);
    var actual = await query(db);
    var passed = EqualityComparer<T>.Default.Equals(actual, expected);
    Console.WriteLine($"  [{(passed ? "PASS" : "FAIL")}] {name}: {actual}{(passed ? "" : $" (expected {expected})")}");
    if (!passed)
    {
        failures++;
    }
}

static string Ids(IEnumerable<Product> products) => string.Join(",", products.Select(p => p.Id));

await CheckAsync(
    "Filter, order and page",
    db => Task.FromResult(Ids(db.Products.AsNoTracking().Where(p => p.IsActive && p.Price < 20m).OrderBy(p => p.Price).ThenBy(p => p.Id).Skip(1).Take(3).ToList())),
    "8,1,6");

var category = Category.Games;
await CheckAsync(
    "Captured enum, descending order, async list",
    async db => Ids(await db.Products.AsNoTracking().Where(p => p.Category == category).OrderByDescending(p => p.Price).ToListAsync(cancellationToken)),
    "2,7,4");

int? noStock = null;
await CheckAsync(
    "Null captured value becomes IS NULL",
    db => Task.FromResult(Ids(db.Products.AsNoTracking().Where(p => p.Stock == noStock).OrderBy(p => p.Id).ToArray())),
    "2,6");

await CheckAsync(
    "Negated nullable comparison follows C#",
    db => Task.FromResult(Ids(db.Products.AsNoTracking().Where(p => !(p.Stock > 10)).OrderBy(p => p.Id).ToList())),
    "2,3,4,6,7");

List<int> wanted = [3, 5, 8, 42];
await CheckAsync(
    "Contains through OPENJSON",
    db => Task.FromResult(Ids(db.Products.AsNoTracking().Where(p => wanted.Contains(p.Id)).OrderBy(p => p.Id).ToList())),
    "3,5,8");

string? search = null;
await CheckAsync(
    "WhereIf skips a null filter",
    db => Task.FromResult(db.Products.WhereIf(search != null, p => p.Name == search).Count()),
    8);

var since = new DateTime(2024, 1, 5, 8, 0, 0);
await CheckAsync(
    "datetime2 parameter keeps its precision",
    db => Task.FromResult(db.Products.LongCount(p => p.CreatedAt > since)),
    4L);

await CheckAsync(
    "First with predicate",
    db => Task.FromResult(db.Products.AsNoTracking().OrderBy(p => p.Id).First(p => p.Category == Category.Music).Name),
    "Abbey Road");

await CheckAsync(
    "SingleOrDefaultAsync without match",
    async db => await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Price > 1000m, cancellationToken) is null,
    true);

await CheckAsync(
    "AnyAsync",
    db => db.Products.AnyAsync(p => !p.IsActive && p.Stock == 3, cancellationToken),
    true);

await CheckAsync(
    "AsNoTrackingWithIdentityResolution tracks nothing",
    db => Task.FromResult($"{db.Products.AsNoTrackingWithIdentityResolution().Where(p => p.IsActive).ToList().Count}|{db.ChangeTracker.Entries().Count()}"),
    "6|0");

await CheckAsync(
    "Include a collection without tracking",
    db =>
    {
        var products = db.Products.AsNoTracking().Include(p => p.Reviews).Where(p => p.Id <= 3).OrderBy(p => p.Id).ToList();
        var linked = products.All(p => p.Reviews.All(r => ReferenceEquals(r.Product, p)));
        return Task.FromResult(string.Join(",", products.Select(p => $"{p.Id}:[{string.Join(" ", p.Reviews.Select(r => r.Id))}]")) + $"|{linked}");
    },
    "1:[11 12],2:[21],3:[]|True");

await CheckAsync(
    "Include a reference without tracking, async",
    async db => string.Join(",", (await db.Reviews.AsNoTracking().Include(r => r.Product).ToListAsync(cancellationToken))
        .OrderBy(r => r.Id)
        .Select(r => r.Product.Name)),
    "Dune,Dune,Hades,Kind of Blue");

static string Invariant(FormattableString value) => FormattableString.Invariant(value);

await CheckAsync(
    "Select to an anonymous type through a navigation",
    async db => string.Join(",", (await db.Reviews.OrderBy(r => r.Id).Select(r => new { r.Id, r.Product.Name }).ToListAsync(cancellationToken))
        .Select(r => $"{r.Id}:{r.Name}")),
    "11:Dune,12:Dune,21:Hades,61:Kind of Blue");

await CheckAsync(
    "Select to a record",
    db => Task.FromResult(string.Join(",", db.Products.Where(p => p.Category == Category.Music).OrderBy(p => p.Id)
        .Select(p => new ProductPrice(p.Id, p.Price)).ToList().Select(p => Invariant($"{p.Id}:{p.Price}")))),
    "3:15.00,6:12.00");

var suffix = "!";
await CheckAsync(
    "Select to an object initializer with computed members",
    db => Task.FromResult(string.Join(",", db.Products.Where(p => p.Id <= 2).OrderBy(p => p.Id)
        .Select(p => new ProductLabel { Id = p.Id, Label = p.Name.ToUpper() + suffix + (p.Stock ?? 0) }).ToList().Select(p => $"{p.Id}:{p.Label}"))),
    "1:DUNE!12,2:HADES!0");

await CheckAsync(
    "Select to a tuple",
    db => Task.FromResult(string.Join(",", db.Products.Where(p => p.Stock == null).OrderBy(p => p.Id).Select(p => ValueTuple.Create(p.Id, p.Stock)).ToList())),
    "(2, ),(6, )");

await CheckAsync(
    "Select one member, then page",
    db => Task.FromResult(string.Join(",", db.Products.OrderBy(p => p.Price).Select(p => p.Name).Skip(1).Take(2).ToList())),
    "Foundation,Dune");

await CheckAsync(
    "Select through a navigation, then FirstAsync",
    async db => await db.Reviews.OrderByDescending(r => r.Stars).ThenBy(r => r.Id).Select(r => r.Product.Name).FirstAsync(cancellationToken),
    "Dune");

await CheckAsync(
    "Select an entity through a navigation, with its include",
    db =>
    {
        var products = db.Reviews.AsNoTracking().Include(r => r.Product).ThenInclude(p => p.Reviews).OrderBy(r => r.Id).Select(r => r.Product).ToList();
        return Task.FromResult(string.Join(",", products.Select(p => $"{p.Id}:{p.Reviews.Count}")) + $"|{ReferenceEquals(products[0], products[1])}");
    },
    "1:2,1:2,2:1,6:1|False");

await CheckAsync(
    "Distinct, then Count",
    db => Task.FromResult(db.Products.Select(p => p.Category).Distinct().Count()),
    3);

await CheckAsync(
    "Distinct values through a navigation",
    db => Task.FromResult(string.Join(",", db.Reviews.Select(r => r.Product.Name).Distinct().ToList().Order(StringComparer.Ordinal))),
    "Dune,Hades,Kind of Blue");

await CheckAsync(
    "Sum, Average, Min and Max",
    async db => Invariant($"{db.Products.Sum(p => p.Price)}|{db.Reviews.Average(r => r.Stars)}|{await db.Products.MinAsync(p => p.Stock, cancellationToken)}|{db.Products.Max(p => p.CreatedAt):yyyy-MM-dd}"),
    "119.48|4.25|0|2024-01-08");

await CheckAsync(
    "Max on no rows throws, as in EF Core",
    db =>
    {
        try
        {
            return Task.FromResult(db.Products.Where(p => p.Price > 1000m).Max(p => p.Id).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult("throws");
        }
    },
    "throws");

await CheckAsync(
    "GroupBy a key with aggregates",
    async db => string.Join(",", (await db.Products.GroupBy(p => p.Category)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(p => p.Price), MaxStock = g.Max(p => p.Stock) })
            .ToListAsync(cancellationToken))
        .OrderBy(x => x.Key)
        .Select(x => Invariant($"{x.Key}:{x.Count}:{x.Total:0.00}:{x.MaxStock}"))),
    "Books:3:25.99:40,Games:3:66.49:8,Music:2:27.00:3");

var cheap = 10m;
await CheckAsync(
    "GroupBy a composite key, with HAVING and a counted predicate",
    db => Task.FromResult(string.Join(",", db.Products.GroupBy(p => new { p.Category, p.IsActive })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.Category, g.Key.IsActive, Cheap = g.Count(p => p.Price < cheap) })
            .ToList()
        .OrderBy(x => x.Category)
        .Select(x => $"{x.Category}:{x.IsActive}:{x.Cheap}"))),
    "Books:True:3,Games:True:0");

await CheckAsync(
    "GroupBy through a navigation, with a distinct count",
    db => Task.FromResult(string.Join(",", db.Reviews.GroupBy(r => r.Product.Name)
            .Select(g => new { g.Key, Stars = g.Select(r => r.Stars).Distinct().Count(), Average = g.Average(r => r.Stars) })
            .OrderBy(x => x.Key)
            .ToList()
        .Select(x => Invariant($"{x.Key}:{x.Stars}:{x.Average}")))),
    "Dune:2:4.5,Hades:1:3,Kind of Blue:1:5");

await CheckAsync(
    "Where, OrderBy and Take after a Select on groups",
    db => Task.FromResult(string.Join(",", db.Products.GroupBy(p => p.Category)
        .Select(g => new { g.Key, Count = g.Count() })
        .Where(x => x.Count >= 3)
        .OrderByDescending(x => x.Key)
        .Take(1)
        .ToList()
        .Select(x => $"{x.Key}:{x.Count}"))),
    "Games:3");

await CheckAsync(
    "GroupBy with a result selector, and Count and Any of groups",
    async db =>
    {
        var maxima = db.Products.GroupBy(p => p.Category, p => p.Price, (key, prices) => new { key, Max = prices.Max() }).ToList();
        var count = db.Products.GroupBy(p => p.Category).Count();
        var any = await db.Products.GroupBy(p => p.Category).AnyAsync(g => g.Count() > 3, cancellationToken);
        return string.Join(",", maxima.OrderBy(x => x.key).Select(x => Invariant($"{x.key}:{x.Max:0.00}"))) + $"|{count}|{any}";
    },
    "Books:9.99,Games:24.50,Music:15.00|3|False");

await CheckAsync(
    "GroupBy returns the groups themselves",
    db =>
    {
        var groups = db.Products.AsNoTracking().GroupBy(p => p.Category).ToList();
        var names = db.Products.GroupBy(p => p.Category, p => p.Name).ToList();
        return Task.FromResult(
            string.Join(",", groups.Select(g => $"{g.Key}:{string.Join(" ", g.Select(p => p.Id).Order())}"))
            + "|" + string.Join(",", names.Select(g => $"{g.Key}:{g.Count()}")));
    },
    "Books:1 5 8,Games:2 4 7,Music:3 6|Books:3,Games:3,Music:2");

await CheckAsync(
    "Last and LastOrDefault read the ordering in reverse",
    async db =>
    {
        var last = db.Products.AsNoTracking().OrderBy(p => p.Price).Last();
        var lastBook = await db.Products.AsNoTracking().OrderBy(p => p.Price).LastOrDefaultAsync(p => p.Category == Category.Books, cancellationToken);
        return $"{last.Name}|{lastBook?.Name}";
    },
    "Hades|Dune");

await CheckAsync(
    "Predicates and orderings through a navigation",
    db => Task.FromResult(string.Join(",", db.Reviews.AsNoTracking()
        .Where(r => r.Product.IsActive && r.Product.Price < 20m)
        .OrderByDescending(r => r.Product.Name)
        .ThenBy(r => r.Id)
        .Select(r => r.Id)
        .ToList())),
    "61,11,12");

await CheckAsync(
    "Find and FindAsync on a no-tracking context",
    async db =>
    {
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var found = db.Products.Find(4);
        var missing = await db.Products.FindAsync([99], cancellationToken);
        return $"{found?.Name}|{missing?.Name ?? "none"}|{db.ChangeTracker.Entries().Count()}";
    },
    "Celeste|none|0");

await CheckAsync(
    "ExecuteDelete through a navigation, rolled back",
    async db =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.Reviews.Where(r => r.Product.Category == Category.Books && r.Stars < 5).ExecuteDeleteAsync(cancellationToken);
        var left = await db.Reviews.CountAsync(cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return $"{deleted}|{left}|{await db.Reviews.CountAsync(cancellationToken)}";
    },
    "1|3|4");

await CheckAsync(
    "Join, then Where, OrderBy and Select through the pair",
    db =>
    {
        var rows = db.Reviews.AsNoTracking()
            .Join(db.Products.Where(p => p.IsActive), r => r.ProductId, p => p.Id, (r, p) => new { r, p })
            .Where(x => x.r.Stars >= 4)
            .OrderBy(x => x.p.Name)
            .ThenBy(x => x.r.Id)
            .Select(x => x.p.Name + ":" + x.r.Stars)
            .ToList();
        var count = db.Reviews.Join(db.Products, r => r.ProductId, p => p.Id, (r, p) => p.Category).Count();
        return Task.FromResult(string.Join(",", rows) + "|" + count);
    },
    "Dune:4,Dune:5,Kind of Blue:5|4");

await CheckAsync(
    "FromSql sent as written, and composed over",
    async db =>
    {
        var cheap = db.Products.FromSql($"SELECT * FROM shop.Products WHERE Price < {10m} ORDER BY Id").AsNoTracking().ToList();
        var books = await db.Products.FromSqlRaw("SELECT * FROM shop.Products WHERE Category = {0}", (int)Category.Books)
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Price)
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);
        return $"{Ids(cheap)}|{string.Join(",", books)}";
    },
    "1,5,8|Dune,Foundation,Neuromancer");

await CheckAsync(
    "Database.SqlQuery of scalars",
    async db =>
    {
        var unstocked = db.Database.SqlQuery<string>($"SELECT Name FROM shop.Products WHERE Stock IS NULL ORDER BY Id").ToList();
        var stars = db.Database.SqlQuery<int>($"SELECT Stars AS Value FROM shop.Reviews").Where(s => s >= 4).Sum();
        var games = await db.Database.SqlQueryRaw<int>("SELECT Id AS Value FROM shop.Products WHERE Category = {0}", (int)Category.Games).CountAsync(cancellationToken);
        return $"{string.Join(",", unstocked)}|{stars}|{games}";
    },
    "Hades,Kind of Blue|14|3");

await CheckAsync(
    "Database.SqlQuery of a record, composed over",
    db => Task.FromResult(string.Join(",", db.Database.SqlQuery<ProductStock>($"SELECT Stock, Name, Id FROM shop.Products")
        .Where(x => x.Stock < 10)
        .OrderBy(x => x.Id)
        .Select(x => x.Name + ":" + x.Stock)
        .ToList())),
    "Abbey Road:3,Celeste:0,Outer Wilds:8");

await CheckAsync(
    "ExecuteSql runs through EF Core, rolled back",
    async db =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await db.Database.ExecuteSqlAsync($"UPDATE shop.Products SET Stock = {99} WHERE Stock IS NULL", cancellationToken);
        var stocked = await db.Products.CountAsync(p => p.Stock == 99, cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return $"{updated}|{stocked}";
    },
    "2|2");

#if NET8_0
// EF Core 8 cannot track entities under Native AOT: tracking queries fail with a clear error instead of EF Core's
// "model building is not supported". Add AsNoTracking(), or move to EF Core 9 or later to track.
await CheckAsync(
    "Tracking query under Native AOT needs EF Core 9",
    db =>
    {
        try
        {
            _ = db.Products.First(p => p.Id == 1);
            return Task.FromResult("tracked");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("EF Core 9", StringComparison.Ordinal))
        {
            return Task.FromResult("rejected");
        }
    },
    RuntimeFeature.IsDynamicCodeSupported ? "tracked" : "rejected");
#else
// EF Core 9 and 10 can track and save changes under Native AOT with a --nativeaot compiled model; EF Core 8 cannot.
await CheckAsync(
    "Tracked query attaches as Unchanged with its shadow value",
    db =>
    {
        var product = db.Products.First(p => p.Id == 1);
        var entry = db.Entry(product);
        return Task.FromResult($"{entry.State}|{entry.Property<int>(ShopDb.Revision).CurrentValue}");
    },
    "Unchanged|10");

await CheckAsync(
    "A second query returns the tracked instance unchanged",
    async db =>
    {
        var first = await db.Products.SingleAsync(p => p.Id == 2, cancellationToken);
        first.Name = "edited";
        var again = (await db.Products.Where(p => p.Category == Category.Games).ToListAsync(cancellationToken)).Single(p => p.Id == 2);
        return $"{ReferenceEquals(first, again)}|{again.Name}";
    },
    "True|edited");

await CheckAsync(
    "Load, modify and save through the change tracker",
    async db =>
    {
        var product = db.Products.Single(p => p.Id == 3);
        product.Price = 16.50m;
        db.Entry(product).Property<int>(ShopDb.Revision).CurrentValue++;
        var saved = await db.SaveChangesAsync(cancellationToken);

        await using var fresh = new ShopDb(options);
        var reloaded = await fresh.Products.FirstAsync(p => p.Id == 3, cancellationToken);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{saved}|{reloaded.Price}|{fresh.Entry(reloaded).Property<int>(ShopDb.Revision).CurrentValue}");
    },
    "1|16.50|31");

await CheckAsync(
    "Tracked Include marks loaded, and an edit saves",
    async db =>
    {
        var product = await db.Products.Include(p => p.Reviews).SingleAsync(p => p.Id == 1, cancellationToken);
        var loaded = db.Entry(product).Collection(p => p.Reviews).IsLoaded;
        product.Reviews[0].Stars = 1;
        var saved = await db.SaveChangesAsync(cancellationToken);

        await using var fresh = new ShopDb(options);
        var stars = (await fresh.Reviews.AsNoTracking().SingleAsync(r => r.Id == 11, cancellationToken)).Stars;
        return $"{loaded}|{saved}|{stars}";
    },
    "True|1|1");

await CheckAsync(
    "Find returns the tracked instance without a second query",
    db =>
    {
        var first = db.Products.Find(3);
        var second = db.Products.Find(3);
        return Task.FromResult($"{ReferenceEquals(first, second)}|{db.Entry(first!).State}");
    },
    "True|Unchanged");

await CheckAsync(
    "FromSql tracks its entities with their shadow values",
    db =>
    {
        var product = db.Products.FromSql($"SELECT * FROM shop.Products WHERE Id = {5}").Single();
        var entry = db.Entry(product);
        return Task.FromResult($"{product.Name}|{entry.State}|{entry.Property<int>(ShopDb.Revision).CurrentValue}");
    },
    "Neuromancer|Unchanged|50");

await CheckAsync(
    "Tracked groups attach their entities",
    db =>
    {
        var groups = db.Products.GroupBy(p => p.Category).ToList();
        return Task.FromResult($"{groups.Count}|{db.ChangeTracker.Entries().Count()}");
    },
    "3|8");

await CheckAsync(
    "Tracked projection attaches its entities and connects them",
    db =>
    {
        var rows = db.Reviews.OrderBy(r => r.Id).Select(r => new { Review = r, r.Product }).ToList();
        var connected = rows.All(row => ReferenceEquals(row.Review.Product, row.Product));
        return Task.FromResult($"{db.ChangeTracker.Entries().Count()}|{ReferenceEquals(rows[0].Product, rows[1].Product)}|{connected}");
    },
    "7|True|True");

await CheckAsync(
    "SaveChanges through EF Core, read back through KetPsi",
    async db =>
    {
        db.Products.Add(new Product { Id = 9, Name = "Snow Crash", Price = 11m, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return (await db.Products.AsNoTracking().SingleAsync(p => p.Id == 9, cancellationToken)).Name;
    },
    "Snow Crash");
#endif

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? Success : Failure;
