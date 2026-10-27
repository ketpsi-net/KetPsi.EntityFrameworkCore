namespace KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

internal static class TestSources
{
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;
        using KetPsi.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore;

        """;

    public const string Model = """
        public sealed class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Rating { get; set; }
            public bool IsActive { get; set; }
            public List<Post> Posts { get; set; } = [];
        }

        public sealed class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
        }

        public sealed class AppDb : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
            public DbSet<Post> Posts => Set<Post>();
        }

        """;

    /// <summary>The model plus a method whose body is <paramref name="body"/>, with <c>db</c> and <c>ct</c> in scope.</summary>
    public static string WithBody(string body)
        => Usings + "namespace App;\n\n" + Model + $$"""
            public static class Queries
            {
                public static async Task RunAsync(AppDb db, CancellationToken ct)
                {
                    await Task.CompletedTask;
                    {{body}}
                }
            }
            """;
}
