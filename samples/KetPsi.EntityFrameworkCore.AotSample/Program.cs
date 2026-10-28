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

#if !NET8_0
// EF Core 9 and 10 can save changes under Native AOT with a --nativeaot compiled model; EF Core 8 cannot.
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
