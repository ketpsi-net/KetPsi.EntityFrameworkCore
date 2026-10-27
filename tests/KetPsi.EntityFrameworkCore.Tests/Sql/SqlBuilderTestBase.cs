using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Sql;

public abstract class SqlBuilderTestBase : IDisposable
{
    protected const string Columns0 = "[t0].[Id], [t0].[IsActive], [t0].[Name], [t0].[Score]";
    protected const string Columns1 = "[t1].[Id], [t1].[IsActive], [t1].[Name], [t1].[Score]";
    protected const string Table = "[shop].[Items] AS [t0]";
    protected const string SelectAll = "SELECT " + Columns0 + " FROM " + Table;

    private readonly ItemContext _db = new();

    protected SqlBuilderTestBase()
    {
        Entity = ModelBinder.GetEntity(_db.Model, typeof(Item));
    }

    internal BoundEntity Entity { get; }

    protected IQueryable<Item> Items => SqlQuery.ToSqlQueryable(_db.Items);

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static IQueryable<Item> Where(IQueryable<Item> source, PredicateTemplate predicate, params object?[] values)
        => SqlQuery.Where(source, predicate, values);

    internal SqlStatement Build(IQueryable<Item> query, QueryTerminal terminal = QueryTerminal.Rows, int? rowLimit = null)
        => SqlBuilder.Build(Entity, ((SqlQueryable<Item>)query).Tail, SqlServerDialect.Instance, terminal, rowLimit);

    internal BoundProperty Property(string name) => Entity.GetProperty(name);
}
