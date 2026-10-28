using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.SqlServer.Tests;

public sealed class SqlServerDialectTests
{
    private static readonly SqlServerDialect Dialect = SqlServerDialect.Instance;

    [Fact]
    public void Instance_IsSingleton()
        => Assert.Same(SqlServerDialect.Instance, SqlServerDialect.Instance);

    [Fact]
    public void ProviderName_MatchesEfCoreSqlServerProvider()
    {
        using var db = new DbContext(new DbContextOptionsBuilder().UseSqlServer("Server=unused").Options);

        Assert.Equal(db.Database.ProviderName, Dialect.ProviderName);
    }

    [Fact]
    public void BooleanLiterals_AreBitCasts()
    {
        Assert.Equal("CAST(1 AS bit)", Dialect.TrueLiteral);
        Assert.Equal("CAST(0 AS bit)", Dialect.FalseLiteral);
    }

    [Theory]
    [InlineData("Blogs", "[Blogs]")]
    [InlineData("Blog Name", "[Blog Name]")]
    [InlineData("odd]name", "[odd]]name]")]
    [InlineData("]]", "[]]]]]")]
    public void QuoteIdentifier_BracketsAndEscapes(string identifier, string expected)
        => Assert.Equal(expected, Dialect.QuoteIdentifier(identifier));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void QuoteIdentifier_MissingIdentifier_Throws(string? identifier)
        => Assert.ThrowsAny<ArgumentException>(() => Dialect.QuoteIdentifier(identifier!));

    [Theory]
    [InlineData("Blogs", "blog", "[blog].[Blogs]")]
    [InlineData("Blogs", null, "[Blogs]")]
    [InlineData("Blogs", "", "[Blogs]")]
    public void QualifyTable_AddsSchemaWhenPresent(string table, string? schema, string expected)
        => Assert.Equal(expected, Dialect.QualifyTable(table, schema));

    [Theory]
    [InlineData(0, "@p0")]
    [InlineData(12, "@p12")]
    public void ParameterName_UsesIndex(int index, string expected)
        => Assert.Equal(expected, Dialect.ParameterName(index));

    [Fact]
    public void ParameterName_NegativeIndex_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>("index", () => Dialect.ParameterName(-1));

    [Theory]
    [InlineData(false, "COUNT(*)")]
    [InlineData(true, "COUNT_BIG(*)")]
    public void Count_UsesBigCountForLongCount(bool isLong, string expected)
        => Assert.Equal(expected, Dialect.Count(isLong));

    [Fact]
    public void Exists_WrapsSubqueryInBooleanCase()
        => Assert.Equal(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM [Blogs]) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
            Dialect.Exists("SELECT 1 FROM [Blogs]"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Exists_MissingSubquery_Throws(string? subquery)
        => Assert.ThrowsAny<ArgumentException>(() => Dialect.Exists(subquery!));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paging_NoSkipOrTake_ReturnsNone(bool hasOrderBy)
        => Assert.Same(SqlPaging.None, Dialect.Paging(offsetParameter: null, limitParameter: null, hasOrderBy));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paging_TakeOnly_UsesTop(bool hasOrderBy)
        => Assert.Equal(
            new SqlPaging("TOP(@p0)", Suffix: null, FallbackOrderBy: null),
            Dialect.Paging(offsetParameter: null, "@p0", hasOrderBy));

    [Theory]
    [InlineData(false, "(SELECT 1)")]
    [InlineData(true, null)]
    public void Paging_SkipOnly_UsesOffset(bool hasOrderBy, string? fallbackOrderBy)
        => Assert.Equal(
            new SqlPaging(SelectPrefix: null, "OFFSET @p0 ROWS", fallbackOrderBy),
            Dialect.Paging("@p0", limitParameter: null, hasOrderBy));

    [Theory]
    [InlineData(false, "(SELECT 1)")]
    [InlineData(true, null)]
    public void Paging_SkipAndTake_UsesOffsetFetch(bool hasOrderBy, string? fallbackOrderBy)
        => Assert.Equal(
            new SqlPaging(SelectPrefix: null, "OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY", fallbackOrderBy),
            Dialect.Paging("@p0", "@p1", hasOrderBy));

    [Fact]
    public void InList_UsesTypedOpenJson()
        => Assert.Equal(
            "[Id] IN (SELECT [value] FROM OPENJSON(@p0) WITH ([value] int '$'))",
            Dialect.InList("[Id]", "@p0", "int"));

    [Theory]
    [InlineData(null, "@p0", "int")]
    [InlineData("", "@p0", "int")]
    [InlineData("[Id]", null, "int")]
    [InlineData("[Id]", "", "int")]
    [InlineData("[Id]", "@p0", null)]
    [InlineData("[Id]", "@p0", "")]
    public void InList_MissingArgument_Throws(string? column, string? listParameter, string? storeType)
        => Assert.ThrowsAny<ArgumentException>(() => Dialect.InList(column!, listParameter!, storeType!));

    [Fact]
    public void InTupleList_MatchesEveryColumnByPosition()
        => Assert.Equal(
            "EXISTS (SELECT 1 FROM OPENJSON(@p0) WITH ([v0] int '$[0]', [v1] nvarchar(10) '$[1]') AS [k] " +
            "WHERE [k].[v0] = [t0].[Floor] AND [k].[v1] = [t0].[Code])",
            Dialect.InTupleList(["[t0].[Floor]", "[t0].[Code]"], "@p0", ["int", "nvarchar(10)"]));

    [Fact]
    public void InTupleList_SingleColumn_StillUsesPosition()
        => Assert.Equal(
            "EXISTS (SELECT 1 FROM OPENJSON(@p0) WITH ([v0] int '$[0]') AS [k] WHERE [k].[v0] = [Id])",
            Dialect.InTupleList(["[Id]"], "@p0", ["int"]));

    [Fact]
    public void InTupleList_NoColumns_Throws()
        => Assert.Throws<ArgumentException>(() => Dialect.InTupleList([], "@p0", []));

    [Fact]
    public void InTupleList_StoreTypeCountMismatch_Throws()
        => Assert.Throws<ArgumentException>(() => Dialect.InTupleList(["[A]", "[B]"], "@p0", ["int"]));

    [Fact]
    public void InTupleList_MissingArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Dialect.InTupleList(null!, "@p0", ["int"]));
        Assert.ThrowsAny<ArgumentException>(() => Dialect.InTupleList(["[A]"], "", ["int"]));
        Assert.Throws<ArgumentNullException>(() => Dialect.InTupleList(["[A]"], "@p0", null!));
    }
}
