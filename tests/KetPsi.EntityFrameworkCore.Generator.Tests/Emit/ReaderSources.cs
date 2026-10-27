using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Emit;

internal static class ReaderSources
{
    /// <summary>An entity with every kind of property a reader handles or skips.</summary>
    public const string Article = """
        public enum Status { Draft, Published }

        public enum Size : byte { Small = 1, Large = 2 }

        public class EntityBase
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; private set; }
        }

        public sealed class Article : EntityBase
        {
            public string Title { get; set; } = "";
            public string? Summary { get; set; }
            public int? Views { get; set; }
            public bool IsPublished { get; set; }
            public long Total { get; set; }
            public short Small { get; set; }
            public byte Tiny { get; set; }
            public float Ratio { get; set; }
            public double Score { get; set; }
            public decimal Price { get; set; }
            public Guid ExternalId { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public DateOnly PublishedOn { get; set; }
            public TimeOnly PublishedAt { get; set; }
            public TimeSpan Duration { get; set; }
            public byte[]? Image { get; set; }
            public Status Status { get; set; }
            public Size? Size { get; set; }
            public string Slug { get; init; } = "";
            public int Secret { get; private set; }

            [System.ComponentModel.DataAnnotations.Schema.NotMapped]
            public string Display { get; set; } = "";

            public string Computed => Title;
            public Author? Author { get; set; }
            public List<Comment> Comments { get; set; } = [];
        }

        public sealed class Author { public int Id { get; set; } }

        public sealed class Comment { public int Id { get; set; } }

        public sealed class Hidden
        {
            private Hidden() { }

            public int Id { get; set; }
        }

        public sealed class ArticleDb : DbContext
        {
            public DbSet<Article> Articles => Set<Article>();
            public DbSet<Hidden> Hidden => Set<Hidden>();
            public DbSet<Author> Authors => Set<Author>();
        }

        public static class Queries
        {
            public static List<Article> Articles(ArticleDb db) => db.Articles.AsNoTracking().ToList();

            public static Hidden? FirstHidden(ArticleDb db) => db.Hidden.AsNoTracking().FirstOrDefault();

            public static List<int> AuthorIds(ArticleDb db) => db.Authors.Select(a => a.Id).ToList();
        }

        public static class Probe
        {
            public static object Read(object reader, System.Data.Common.DbDataReader data, int[] ordinals)
                => ((KetPsi.EntityFrameworkCore.Infrastructure.IEntityReader<Article>)reader).Read(data, ordinals);

            public static object ReadHidden(object reader, System.Data.Common.DbDataReader data, int[] ordinals)
                => ((KetPsi.EntityFrameworkCore.Infrastructure.IEntityReader<Hidden>)reader).Read(data, ordinals);
        }
        """;

    public static string Source => TestSources.Usings + "namespace App;\n\n" + Article;
}
