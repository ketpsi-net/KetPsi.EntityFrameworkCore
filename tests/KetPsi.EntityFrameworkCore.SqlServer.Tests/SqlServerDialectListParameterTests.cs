using System.Text.Json;

namespace KetPsi.EntityFrameworkCore.SqlServer.Tests;

public sealed class SqlServerDialectListParameterTests
{
    private static readonly SqlServerDialect Dialect = SqlServerDialect.Instance;

    public enum UnsignedEnum : ulong
    {
        Max = ulong.MaxValue,
    }

    public static TheoryData<object, string> StringValues => new()
    {
        { "plain", "plain" },
        { "quote \" and ' and é", "quote \" and ' and é" },
        { 'x', "x" },
        { new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), "0f8fad5b-d9cb-469f-a165-70867728950e" },
        { new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567), "2024-01-02T03:04:05.1234567" },
        { new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local), "2024-01-02T03:04:05.0000000" },
        { new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1234567), "2024-01-02T03:04:05.1234567+02:00" },
        { new DateOnly(2024, 1, 2), "2024-01-02" },
        { new TimeOnly(3, 4, 5).Add(TimeSpan.FromTicks(1234567)), "03:04:05.1234567" },
        { new TimeSpan(0, 3, 4, 5).Add(TimeSpan.FromTicks(1234567)), "03:04:05.1234567" },
    };

    public static TheoryData<object, string> NumberValues => new()
    {
        { (byte)200, "200" },
        { (sbyte)-5, "-5" },
        { (short)-300, "-300" },
        { (ushort)60000, "60000" },
        { -70000, "-70000" },
        { 4000000000u, "4000000000" },
        { -9000000000L, "-9000000000" },
        { ulong.MaxValue, "18446744073709551615" },
        { 1.5f, "1.5" },
        { 2.25d, "2.25" },
        { 3.125m, "3.125" },
        { DayOfWeek.Friday, "5" },
        { UnsignedEnum.Max, "18446744073709551615" },
    };

    [Fact]
    public void CreateListParameterValue_Empty_ReturnsEmptyArray()
        => Assert.Equal("[]", Dialect.CreateListParameterValue([]));

    [Fact]
    public void CreateListParameterValue_Integers_ReturnsJsonArray()
        => Assert.Equal("[1,2,3]", Dialect.CreateListParameterValue([1, 2, 3]));

    [Fact]
    public void CreateListParameterValue_Null_WritesJsonNull()
        => Assert.Equal("[1,null]", Dialect.CreateListParameterValue([1, null]));

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void CreateListParameterValue_Boolean_WritesJsonBoolean(bool value, string expected)
        => Assert.Equal($"[{expected}]", Dialect.CreateListParameterValue([value]));

    [Theory]
    [MemberData(nameof(StringValues))]
    public void CreateListParameterValue_TextValues_WriteSqlServerFormats(object value, string expected)
    {
        var element = SingleElement(value);

        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.Equal(expected, element.GetString());
    }

    [Theory]
    [MemberData(nameof(NumberValues))]
    public void CreateListParameterValue_NumericValues_WriteJsonNumbers(object value, string expected)
    {
        var element = SingleElement(value);

        Assert.Equal(JsonValueKind.Number, element.ValueKind);
        Assert.Equal(expected, element.GetRawText());
    }

    [Fact]
    public void CreateListParameterValue_UnsupportedType_ThrowsNamingType()
    {
        var exception = Assert.Throws<NotSupportedException>(() => Dialect.CreateListParameterValue([new byte[] { 1 }]));

        Assert.Contains(typeof(byte[]).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateListParameterValue_NullValues_Throws()
        => Assert.Throws<ArgumentNullException>("values", () => Dialect.CreateListParameterValue(null!));

    private static JsonElement SingleElement(object value)
    {
        var json = Assert.IsType<string>(Dialect.CreateListParameterValue([value]));

        using var document = JsonDocument.Parse(json);
        return Assert.Single(document.RootElement.EnumerateArray().ToArray()).Clone();
    }
}
