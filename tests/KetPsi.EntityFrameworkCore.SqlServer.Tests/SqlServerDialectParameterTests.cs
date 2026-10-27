using System.Data;
using Microsoft.Data.SqlClient;

namespace KetPsi.EntityFrameworkCore.SqlServer.Tests;

public sealed class SqlServerDialectParameterTests
{
    private static readonly SqlServerDialect Dialect = SqlServerDialect.Instance;

    [Theory]
    [InlineData("bit", DbType.Boolean)]
    [InlineData("tinyint", DbType.Byte)]
    [InlineData("smallint", DbType.Int16)]
    [InlineData("int", DbType.Int32)]
    [InlineData("bigint", DbType.Int64)]
    [InlineData("real", DbType.Single)]
    [InlineData("float", DbType.Double)]
    [InlineData("uniqueidentifier", DbType.Guid)]
    [InlineData("date", DbType.Date)]
    [InlineData("time", DbType.Time)]
    [InlineData("time(3)", DbType.Time)]
    [InlineData("datetime", DbType.DateTime)]
    [InlineData("smalldatetime", DbType.DateTime)]
    [InlineData("datetime2", DbType.DateTime2)]
    [InlineData("datetime2(7)", DbType.DateTime2)]
    [InlineData("datetimeoffset", DbType.DateTimeOffset)]
    [InlineData("INT", DbType.Int32)]
    public void ConfigureParameter_SimpleStoreType_SetsDbType(string storeType, DbType expected)
        => Assert.Equal(expected, Configure(storeType, value: 1).DbType);

    [Theory]
    [InlineData("nvarchar(100)", DbType.String, 100)]
    [InlineData("nvarchar(max)", DbType.String, -1)]
    [InlineData("varchar(20)", DbType.AnsiString, 20)]
    [InlineData("varchar(MAX)", DbType.AnsiString, -1)]
    [InlineData("nchar(10)", DbType.StringFixedLength, 10)]
    [InlineData("char(3)", DbType.AnsiStringFixedLength, 3)]
    public void ConfigureParameter_StringStoreType_SetsDbTypeAndSize(string storeType, DbType dbType, int size)
    {
        var parameter = Configure(storeType, "x");

        Assert.Equal(dbType, parameter.DbType);
        Assert.Equal(size, parameter.Size);
    }

    [Theory]
    [InlineData("decimal(18,2)", 18, 2)]
    [InlineData("numeric(10, 4)", 10, 4)]
    public void ConfigureParameter_DecimalStoreType_SetsPrecisionAndScale(string storeType, byte precision, byte scale)
    {
        var parameter = Configure(storeType, 1.5m);

        Assert.Equal(DbType.Decimal, parameter.DbType);
        Assert.Equal(precision, parameter.Precision);
        Assert.Equal(scale, parameter.Scale);
    }

    // Without facets the dialect sets only the type; SqlClient derives precision and scale from the value.
    [Fact]
    public void ConfigureParameter_DecimalWithoutFacets_SetsDbType()
        => Assert.Equal(DbType.Decimal, Configure("decimal", 1.5m).DbType);

    [Fact]
    public void ConfigureParameter_UnknownStoreType_KeepsInferredType()
        => Assert.Equal(DbType.Int32, Configure("sql_variant", 5).DbType);

    [Fact]
    public void ConfigureParameter_NoStoreTypeForString_UsesUnlimitedNvarchar()
    {
        var parameter = Configure(storeType: null, "[1,2]");

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(-1, parameter.Size);
    }

    [Fact]
    public void ConfigureParameter_NoStoreTypeForNumber_KeepsInferredType()
        => Assert.Equal(DbType.Int32, Configure(storeType: null, 10).DbType);

    [Fact]
    public void ConfigureParameter_Null_Throws()
        => Assert.Throws<ArgumentNullException>("parameter", () => Dialect.ConfigureParameter(null!, "int"));

    private static SqlParameter Configure(string? storeType, object value)
    {
        var parameter = new SqlParameter("@p0", value);
        Dialect.ConfigureParameter(parameter, storeType);
        return parameter;
    }
}
