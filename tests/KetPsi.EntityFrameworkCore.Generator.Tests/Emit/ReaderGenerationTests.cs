using System.Data;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

public sealed class ReaderGenerationTests
{
    [Fact]
    public void Readers_MatchSnapshot()
    {
        var run = GeneratorHost.Run(ReaderSources.Source);

        Snapshot.Match(run.Generated(ReaderEmitter.HintName), "Readers");
    }

    [Fact]
    public void Readers_CompileWithoutWarnings()
    {
        var run = GeneratorHost.Run(ReaderSources.Source);

        // The sample uses Select on Authors on purpose, to show entities with only unsupported operators get no reader.
        Assert.Equal("Select", run.TextAt(Assert.Single(run.Diagnostics)));
        Assert.Empty(run.OutputProblems());
    }

    [Fact]
    public void Readers_WithoutSqlServerPackage_DoNotRegisterDialect()
    {
        var compilation = GeneratorHost.Compile("NoSqlServer", ReaderSources.Source, GeneratorHost.SqlServerDialectAssembly);

        var generated = GeneratorHost.Run(compilation).Generated(ReaderEmitter.HintName);

        Assert.DoesNotContain("SqlDialects.Register", generated, StringComparison.Ordinal);
        Assert.Contains("EntityReaders.Register(new App_Article_Reader())", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Readers_OnlyForEntitiesWithSupportedOperators()
    {
        var generated = GeneratorHost.Run(ReaderSources.Source).Generated(ReaderEmitter.HintName);

        Assert.Contains("App_Article_Reader", generated, StringComparison.Ordinal);
        Assert.Contains("App_Hidden_Reader", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("App_Author_Reader", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Readers_OneReaderPerEntityAcrossQueries()
    {
        var run = GeneratorHost.Run(TestSources.WithBody(
            "var a = db.Blogs.AsNoTracking().ToList(); var b = db.Blogs.AsNoTracking().Count(); var c = db.Blogs.Take(1);"));

        var generated = run.Generated(ReaderEmitter.HintName);

        Assert.Equal(1, CountOccurrences(generated, "file sealed class App_Blog_Reader"));
    }

    [Fact]
    public void NoSupportedOperators_GeneratesNothing()
        => Assert.Empty(GeneratorHost.Run(TestSources.WithBody("var r = db.Blogs.Select(b => b.Id);")).Result.GeneratedTrees);

    [Fact]
    public void GeneratedReader_ReadsEveryPropertyKind()
    {
        var run = GeneratorHost.Run(Compile("ReaderExecution"));
        var assembly = run.Load();
        var articleType = assembly.GetType("App.Article")!;
        var reader = GetRegisteredReader(articleType);
        var names = (IReadOnlyList<string>)reader.GetType().GetProperty(nameof(IEntityReader<object>.PropertyNames))!.GetValue(reader)!;

        var values = new Dictionary<string, object?>
        {
            ["Id"] = 7,
            ["CreatedAt"] = new DateTime(2024, 1, 2, 3, 4, 5),
            ["Title"] = "Hello",
            ["Summary"] = null,
            ["Views"] = 42,
            ["IsPublished"] = true,
            ["Total"] = 9_000_000_000L,
            ["Small"] = (short)-3,
            ["Tiny"] = (byte)250,
            ["Ratio"] = 0.5f,
            ["Score"] = 2.25d,
            ["Price"] = 19.99m,
            ["ExternalId"] = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
            ["UpdatedAt"] = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)),
            ["PublishedOn"] = new DateOnly(2024, 5, 6),
            ["PublishedAt"] = new TimeOnly(7, 8, 9),
            ["Duration"] = TimeSpan.FromMinutes(90),
            ["Image"] = new byte[] { 1, 2, 3 },
            ["Status"] = 1,
            ["Size"] = (byte)2,
            ["Slug"] = "hello",
            ["Secret"] = 99,
        };
        Assert.Equal(values.Keys.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));

        var article = ReadRow(assembly, "Read", reader, names, values);

        foreach (var (name, expected) in values)
        {
            var actual = articleType.GetProperty(name)!.GetValue(article);
            var expectedValue = name switch
            {
                "Status" => Enum.ToObject(articleType.GetProperty(name)!.PropertyType, expected!),
                "Size" => Enum.ToObject(Nullable.GetUnderlyingType(articleType.GetProperty(name)!.PropertyType)!, expected!),
                _ => expected,
            };
            Assert.Equal(expectedValue, actual);
        }
    }

    [Fact]
    public void GeneratedReader_UnmappedProperty_KeepsInitializerValue()
    {
        var run = GeneratorHost.Run(Compile("ReaderUnmapped"));
        var assembly = run.Load();
        var articleType = assembly.GetType("App.Article")!;
        var reader = GetRegisteredReader(articleType);
        var names = (IReadOnlyList<string>)reader.GetType().GetProperty(nameof(IEntityReader<object>.PropertyNames))!.GetValue(reader)!;

        // Only Id is mapped; every other ordinal is -1.
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(5);
        var ordinals = names.Select(n => n == "Id" ? 0 : -1).ToArray();

        var article = Read(assembly, "Read", reader, table, ordinals);

        Assert.Equal(5, articleType.GetProperty("Id")!.GetValue(article));
        Assert.Equal("", articleType.GetProperty("Title")!.GetValue(article));
        Assert.Null(articleType.GetProperty("Views")!.GetValue(article));
    }

    [Fact]
    public void GeneratedReader_PrivateConstructor_UsesAccessor()
    {
        var run = GeneratorHost.Run(Compile("ReaderPrivateCtor"));
        var assembly = run.Load();
        var hiddenType = assembly.GetType("App.Hidden")!;
        var reader = GetRegisteredReader(hiddenType);
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(11);

        var hidden = Read(assembly, "ReadHidden", reader, table, [0]);

        Assert.Equal(11, hiddenType.GetProperty("Id")!.GetValue(hidden));
    }

    [Fact]
    public void UnsupportedPropertyType_ReportsKP3001AndSkipsReader()
    {
        var run = GeneratorHost.Run(TestSources.Usings + """
            namespace App;

            public enum Flags : uint { None = 0 }

            public sealed class Device
            {
                public int Id { get; set; }
                public uint Code { get; set; }
                public Flags Flags { get; set; }
            }

            public sealed class Db : DbContext { public DbSet<Device> Devices => Set<Device>(); }

            public static class Queries { public static List<Device> Run(Db db) => db.Devices.AsNoTracking().ToList(); }
            """);

        Assert.Equal(["KP3001", "KP3001"], run.Diagnostics.Select(d => d.Id));
        Assert.Equal(["Code", "Flags"], run.Diagnostics.Select(run.TextAt).Order(StringComparer.Ordinal));
        Assert.Equal(
            "The property 'App.Device.Code' has the type 'uint', which KetPsi.EntityFrameworkCore cannot read yet. Mark it [NotMapped] if EF Core does not map it.",
            run.Diagnostics.Single(d => run.TextAt(d) == "Code").GetMessage());
        Assert.Empty(run.Result.GeneratedTrees);
    }

    [Theory]
    [InlineData("public sealed class Thing { public Thing(int id) { Id = id; } public int Id { get; set; } }")]
    [InlineData("public abstract class Thing { public int Id { get; set; } }")]
    [InlineData("public sealed class Thing { private Thing() { } public int Id { get; init; } }")]
    public void UnconstructibleEntity_ReportsKP3004(string entity)
    {
        var run = GeneratorHost.Run(TestSources.Usings + "namespace App;\n\n" + entity + """

            public sealed class Db : DbContext { public DbSet<Thing> Things => Set<Thing>(); }

            public static class Queries { public static int Run(Db db) => db.Things.Count(); }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3004", diagnostic.Id);
        Assert.Equal("Thing", run.TextAt(diagnostic));
        Assert.Empty(run.Result.GeneratedTrees);
    }

    private static Microsoft.CodeAnalysis.Compilation Compile(string assemblyName)
        => GeneratorHost.Compile(assemblyName, ReaderSources.Source);

    private static object GetRegisteredReader(Type entityType)
        => typeof(EntityReaders).GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entityType)
            .Invoke(null, null)!;

    private static object ReadRow(Assembly assembly, string probe, object reader, IReadOnlyList<string> names, Dictionary<string, object?> values)
    {
        var table = new DataTable();
        foreach (var name in names)
        {
            var value = values[name];
            table.Columns.Add(name, value?.GetType() ?? typeof(string));
        }

        table.Rows.Add([.. names.Select(n => values[n] ?? DBNull.Value)]);
        return Read(assembly, probe, reader, table, [.. Enumerable.Range(0, names.Count)]);
    }

    private static object Read(Assembly assembly, string probe, object reader, DataTable table, int[] ordinals)
    {
        using var data = table.CreateDataReader();
        Assert.True(data.Read());
        return assembly.GetType("App.Probe")!.GetMethod(probe)!.Invoke(null, [reader, data, ordinals])!;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
