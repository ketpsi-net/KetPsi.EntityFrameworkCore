using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests;

/// <summary>
/// Properties with value converters: the generator reads them as their CLR type, and the runtime converts the column's
/// value. A struct only a converter can store, such as a strongly typed key, is read with <c>GetFieldValue&lt;T&gt;</c>.
/// </summary>
public sealed class ValueConverterGenerationTests
{
    private const string Model = """
        namespace App;

        public readonly record struct TicketId(int Value);

        public enum State { Open, Closed }

        public sealed class Ticket
        {
            public TicketId Id { get; set; }
            public TicketId? ParentId { get; set; }
            public State State { get; set; }
            public bool IsUrgent { get; set; }
            public string Title { get; set; } = "";
            public List<Note> Notes { get; } = [];
        }

        public sealed class Note
        {
            public int Id { get; set; }
            public TicketId TicketId { get; set; }
            public Ticket Ticket { get; set; } = null!;
        }

        public sealed record TicketRow(TicketId Id, string Title);

        public sealed class Db : DbContext
        {
            public DbSet<Ticket> Tickets => Set<Ticket>();
            public DbSet<Note> Notes => Set<Note>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Ticket>().Property(t => t.Id).HasConversion(id => id.Value, v => new TicketId(v));
                modelBuilder.Entity<Ticket>().Property(t => t.State).HasConversion<string>();
                modelBuilder.Entity<Ticket>().Property(t => t.IsUrgent).HasConversion(v => v ? "Y" : "N", v => v == "Y");
            }

            protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                => configurationBuilder.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        }

        public sealed class TicketIdConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TicketId, int>(id => id.Value, v => new TicketId(v));

        """;

    [Fact]
    public void ConvertedProperties_AreReadAndQueried_WithoutDiagnostics()
    {
        var run = GeneratorHost.Run(TestSources.Usings + Model + """
            public static class Queries
            {
                public static List<Ticket> ById(Db db, TicketId id) => db.Tickets.Where(t => t.Id == id && t.State == State.Open && t.IsUrgent).ToList();
                public static List<Ticket> ByIds(Db db, List<TicketId> ids) => db.Tickets.Where(t => ids.Contains(t.Id) && t.ParentId != null).OrderBy(t => t.Id).ToList();
                public static List<TicketId> Ids(Db db) => db.Tickets.Select(t => t.Id).ToList();
                public static object Groups(Db db) => db.Tickets.GroupBy(t => t.ParentId).Select(g => new { g.Key, Count = g.Count() }).ToList();
                public static object Joined(Db db) => db.Notes.Join(db.Tickets, n => n.TicketId, t => t.Id, (n, t) => new { n.Id, t.Title }).ToList();
                public static Ticket? Find(Db db) => db.Tickets.Find(new TicketId(1));
                public static List<Ticket> WithNotes(Db db) => db.Tickets.Include(t => t.Notes).ToList();
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.OutputProblems());
        var readers = run.Generated(ReaderEmitter.HintName);
        Assert.Contains("reader.GetFieldValue<global::App.TicketId>(", readers, StringComparison.Ordinal);
        Assert.Contains("ParentId = reader.IsDBNull(ordinals[", readers, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberOfAConvertedValue_IsNotSupported_AsInEfCore()
    {
        var run = GeneratorHost.Run(TestSources.Usings + Model + """
            public static class Queries { public static List<Ticket> Run(Db db) => db.Tickets.Where(t => t.Id.Value == 1).ToList(); }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP2003", diagnostic.Id);
        Assert.Equal("t.Id.Value", run.TextAt(diagnostic));
    }

    [Theory]
    [InlineData("db.Database.SqlQuery<TicketId>($\"SELECT Id AS Value FROM Tickets\").ToList()", "SqlQuery")]
    [InlineData("db.Database.SqlQuery<TicketRow>($\"SELECT Id, Title FROM Tickets\").ToList()", "SqlQuery")]
    public void SqlQueryOfAConvertedStruct_ReportsKP1004(string query, string text)
    {
        var run = GeneratorHost.Run(TestSources.Usings + Model + $$"""
            public static class Queries { public static object Run(Db db) => {{query}}; }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP1004", diagnostic.Id);
        Assert.Equal(text, run.TextAt(diagnostic));
        Assert.Contains("value converter", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "modelBuilder.Entity<Holder>().Property(h => h.Address).HasConversion(a => a.Text, v => new Address(v));",
        "the property type 'App.Address' is a class, which KetPsi.EntityFrameworkCore reads as a navigation; only value types, strings and byte arrays can be converted yet")]
    [InlineData(
        "modelBuilder.Entity<Holder>().Property(\"Code\").HasConversion<string>();",
        "configure the property with the generic Property(x => x.Name) or Properties<T>(), so its type is known at build time")]
    public void ConverterKetPsiCannotRead_ReportsKP3002(string configuration, string reason)
    {
        var run = GeneratorHost.Run(TestSources.Usings + $$"""
            namespace App;

            public sealed class Address(string text) { public string Text { get; } = text; }

            public sealed class Holder
            {
                public int Id { get; set; }
                public int Code { get; set; }
                public Address Address { get; set; } = null!;
            }

            public sealed class HolderDb : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    {{configuration}}
                }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3002", diagnostic.Id);
        Assert.Equal("HasConversion", run.TextAt(diagnostic));
        Assert.Equal($"'HasConversion' configures a value converter KetPsi.EntityFrameworkCore cannot read yet: {reason}", diagnostic.GetMessage());
    }

    [Fact]
    public void DefaultTypeMappingConverter_ReportsKP3002()
    {
        var run = GeneratorHost.Run(TestSources.Usings + Model.Replace(
            "=> configurationBuilder.Properties<TicketId>().HaveConversion<TicketIdConverter>();",
            "=> configurationBuilder.DefaultTypeMapping<TicketId>().HasConversion<TicketIdConverter>();",
            StringComparison.Ordinal));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("KP3002", diagnostic.Id);
        Assert.Contains("SqlQuery", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
