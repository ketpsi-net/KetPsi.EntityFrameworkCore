using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Sql;

namespace KetPsi.EntityFrameworkCore.Tests.Infrastructure;

// The registry is process-wide, so every test uses its own provider name.
public sealed class SqlDialectsTests
{
    [Fact]
    public void Register_ThenResolve_ReturnsDialect()
    {
        var dialect = new FakeDialect(UniqueProviderName());

        SqlDialects.Register(dialect);

        Assert.Same(dialect, SqlDialects.Resolve(dialect.ProviderName));
    }

    [Fact]
    public void Register_SameDialectTypeAgain_KeepsFirstRegistration()
    {
        var providerName = UniqueProviderName();
        var first = new FakeDialect(providerName);

        SqlDialects.Register(first);
        SqlDialects.Register(new FakeDialect(providerName));

        Assert.Same(first, SqlDialects.Resolve(providerName));
    }

    [Fact]
    public void Register_DifferentDialectTypeForSameProvider_Throws()
    {
        var providerName = UniqueProviderName();
        SqlDialects.Register(new FakeDialect(providerName));

        var exception = Assert.Throws<InvalidOperationException>(() => SqlDialects.Register(new OtherFakeDialect(providerName)));

        Assert.Contains(providerName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FakeDialect).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(OtherFakeDialect).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_NullDialect_Throws()
        => Assert.Throws<ArgumentNullException>("dialect", () => SqlDialects.Register(null!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Register_MissingProviderName_Throws(string? providerName)
        => Assert.ThrowsAny<ArgumentException>(() => SqlDialects.Register(new FakeDialect(providerName!)));

    [Fact]
    public void Resolve_UnregisteredProvider_ThrowsNamingProvider()
    {
        var providerName = UniqueProviderName();

        var exception = Assert.Throws<InvalidOperationException>(() => SqlDialects.Resolve(providerName));

        Assert.Contains(providerName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NullProvider_Throws()
        => Assert.Throws<InvalidOperationException>(() => SqlDialects.Resolve(null));

    [Fact]
    public void SqlPagingNone_HasNoClauses()
        => Assert.Equal(new SqlPaging(SelectPrefix: null, Suffix: null, FallbackOrderBy: null), SqlPaging.None);

    private static string UniqueProviderName() => "Test.Provider." + Guid.NewGuid().ToString("N");

    private class FakeDialect(string providerName) : ISqlDialect
    {
        public string ProviderName { get; } = providerName;

        public string TrueLiteral => throw new NotSupportedException();

        public string FalseLiteral => throw new NotSupportedException();

        public string QuoteIdentifier(string identifier) => throw new NotSupportedException();

        public string QualifyTable(string table, string? schema) => throw new NotSupportedException();

        public string ParameterName(int index) => throw new NotSupportedException();

        public string Count(bool isLong) => throw new NotSupportedException();

        public string Exists(string subquery) => throw new NotSupportedException();

        public SqlPaging Paging(string? offsetParameter, string? limitParameter, bool hasOrderBy) => throw new NotSupportedException();

        public string InList(string column, string listParameter, string storeType) => throw new NotSupportedException();

        public object CreateListParameterValue(IEnumerable<object?> values) => throw new NotSupportedException();
    }

    private sealed class OtherFakeDialect(string providerName) : FakeDialect(providerName);
}
