// FromSql on a set and Database.SqlQuery, sent as written or composed over, compiled into both query builds like the
// rest of the catalog.
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Differential;

public sealed record RawBlogRow(int Id, string Name, int? Views);

public sealed record RawViewsRow(int Id, int Views);

public sealed class RawRatingRow
{
    public int Rating { get; init; }

    public string Name { get; set; } = "";
}

public static partial class Queries
{
    // Blog has query filters, so they compose over its SQL; every column is read by name.
    public static List<Blog> FromSqlUnderFilters(DiffDb db, int rating)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Rating >= {rating}").AsNoTracking().ToList();

    public static List<Blog> FromSqlReorderedColumns(DiffDb db)
        => db.Blogs.FromSql($"SELECT TenantId, Revision, Name, Id, IsDeleted, Rating, Views, Status, CreatedAt, Price, ExternalId, IsActive, AuthorId FROM diff.Blogs").AsNoTracking().ToList();

    public static List<Blog> FromSqlTracked(DiffDb db) => db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Id IN (1, 2)").ToList();

    // Blog has query filters, so EF Core composes over its SQL to apply them; Author has none, so its SQL runs as written.
    public static List<Blog> FromSqlMissingColumn(DiffDb db) => db.Blogs.FromSql($"SELECT Id, Name FROM diff.Blogs").AsNoTracking().ToList();

    public static List<Author> FromSqlRunsAsWritten(DiffDb db, int id)
        => db.Authors.FromSql($"SELECT Name, 1 AS Extra, ID FROM diff.Authors WHERE Id > {id}").AsNoTracking().ToList();

    public static List<Author> FromSqlStoredProcedure(DiffDb db)
        => db.Authors.FromSql($"EXEC sp_executesql N'SELECT * FROM diff.Authors WHERE Id > 1'").AsNoTracking().ToList();

    public static List<Author> FromSqlAsWrittenMissingColumn(DiffDb db) => db.Authors.FromSql($"SELECT Id FROM diff.Authors").AsNoTracking().ToList();

    public static List<Author> FromSqlAsWrittenTracked(DiffDb db) => db.Authors.FromSqlRaw("SELECT * FROM diff.Authors WHERE Name <> {0}", "Ann").ToList();

    // Composed: the filters and the operators after it apply to the SQL as a subquery.
    public static List<Blog> FromSqlComposed(DiffDb db, int rating)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Rating >= {rating}").AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).ToList();

    public static List<Blog> FromSqlIgnoringFilters(DiffDb db, int rating)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs").IgnoreQueryFilters().AsNoTracking().Where(b => b.Rating == rating).ToList();

    public static List<Blog> FromSqlRawArguments(DiffDb db, string name, int views)
        => db.Blogs.FromSqlRaw("SELECT * FROM diff.Blogs WHERE Name = {0} OR Views > {1}", name, views).AsNoTracking().Where(b => b.Rating > 1).ToList();

    public static List<Blog> FromSqlDbParameter(DiffDb db, int rating, int views)
        => db.Blogs.FromSqlRaw("SELECT * FROM diff.Blogs WHERE Rating = @p0", new SqlParameter("p0", rating)).AsNoTracking().Where(b => b.Views > views).ToList();

    public static List<Blog> FromSqlInterpolatedWithComment(DiffDb db, int rating)
        => db.Blogs.FromSqlInterpolated($"-- the blogs\n/* rated */ SELECT * FROM diff.Blogs WHERE Rating > {rating}").AsNoTracking().ToList();

    public static List<Blog> FromSqlNonComposable(DiffDb db)
        => db.Blogs.FromSql($"EXEC sp_executesql N'SELECT * FROM diff.Blogs'").AsNoTracking().Where(b => b.IsActive).ToList();

    public static List<Blog> FromSqlInclude(DiffDb db, int rating)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Rating = {rating}").AsNoTracking().Include(b => b.Posts).Include(b => b.Author).ToList();

    public static object FromSqlSelect(DiffDb db)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs").Where(b => b.Views != null).Select(b => new { b.Id, b.Name, Author = b.Author!.Name }).ToList();

    public static int FromSqlCount(DiffDb db, int rating) => db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Rating >= {rating}").Count();

    public static Task<Blog> FromSqlFirstAsync(DiffDb db)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs").AsNoTracking().OrderByDescending(b => b.Price).FirstAsync();

    public static object FromSqlGroupBy(DiffDb db)
        => db.Blogs.FromSql($"SELECT * FROM diff.Blogs").GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();

    public static object FromSqlJoinInner(DiffDb db, int rating)
        => db.Posts.Join(db.Blogs.FromSql($"SELECT * FROM diff.Blogs WHERE Rating >= {rating}"), p => p.BlogId, b => b.Id, (p, b) => new { p.Title, b.Name }).ToList();

    public static object FromSqlExecuteDelete(DiffDb db, int postId)
        => InRolledBackTransaction(db, () => db.Comments.FromSql($"SELECT * FROM diff.Comments WHERE PostId = {postId}").ExecuteDelete());

    // Database.SqlQuery of a scalar: the first column as written, the column Value when composed over.
    public static List<string> SqlQueryFirstColumn(DiffDb db) => db.Database.SqlQuery<string>($"SELECT Name, Id FROM diff.Blogs").ToList();

    public static List<int> SqlQueryComposed(DiffDb db, int rating)
        => db.Database.SqlQuery<int>($"SELECT Rating AS Value FROM diff.Blogs WHERE TenantId = 1").Where(v => v > rating).ToList();

    public static List<int> SqlQueryWithoutValue(DiffDb db) => db.Database.SqlQuery<int>($"SELECT Id FROM diff.Blogs").Where(v => v > 3).ToList();

    public static List<int?> SqlQueryNullable(DiffDb db) => db.Database.SqlQuery<int?>($"SELECT Views AS Value FROM diff.Blogs").ToList();

    public static int SqlQueryNullableCount(DiffDb db, int views)
        => db.Database.SqlQuery<int?>($"SELECT Views AS Value FROM diff.Blogs").Count(v => v > views);

    public static List<int> SqlQueryNullIntoInt(DiffDb db) => db.Database.SqlQuery<int>($"SELECT Views AS Value FROM diff.Blogs").ToList();

    public static Task<List<int>> SqlQueryRawAsync(DiffDb db, int rating)
        => db.Database.SqlQueryRaw<int>("SELECT Id AS Value FROM diff.Blogs WHERE Rating > {0}", rating).OrderBy(v => v).Take(3).ToListAsync();

    public static Task<decimal> SqlQueryFirstAsync(DiffDb db) => db.Database.SqlQuery<decimal>($"SELECT Price AS Value FROM diff.Blogs").OrderByDescending(v => v).FirstAsync();

    public static List<Status> SqlQueryEnumDistinct(DiffDb db) => db.Database.SqlQuery<Status>($"SELECT Status AS Value FROM diff.Blogs").Distinct().ToList();

    public static List<int> SqlQueryNonComposable(DiffDb db) => db.Database.SqlQuery<int>($"EXEC sp_executesql N'SELECT 1 AS Value'").Where(v => v > 0).ToList();

    // Database.SqlQuery of an unmapped type: the columns named like its properties.
    public static List<RawBlogRow> SqlQueryRecord(DiffDb db) => db.Database.SqlQuery<RawBlogRow>($"SELECT Views, name, Id FROM diff.Blogs").ToList();

    public static List<string> SqlQueryRecordComposed(DiffDb db, int id)
        => db.Database.SqlQuery<RawBlogRow>($"SELECT * FROM diff.Blogs").Where(x => x.Id > id && x.Views != null).OrderBy(x => x.Id).Select(x => x.Name + "!").ToList();

    public static object SqlQueryClass(DiffDb db, int rating)
        => db.Database.SqlQuery<RawRatingRow>($"SELECT Rating, Name FROM diff.Blogs WHERE IsDeleted = 0").Where(x => x.Rating >= rating).Select(x => new { x.Name, x.Rating }).ToList();

    public static List<RawBlogRow> SqlQueryMissingColumn(DiffDb db) => db.Database.SqlQuery<RawBlogRow>($"SELECT Id, Name FROM diff.Blogs").ToList();

    public static List<RawViewsRow> SqlQueryNullIntoProperty(DiffDb db) => db.Database.SqlQuery<RawViewsRow>($"SELECT Id, Views FROM diff.Blogs").ToList();
}
