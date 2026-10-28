using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace KetPsi.EntityFrameworkCore.TestUtilities;

public sealed class FakeDbCommand(FakeDbConnection connection) : DbCommand
{
    // A real SqlParameterCollection, so parameters behave exactly as they do against SQL Server.
    private readonly SqlCommand _parameterHolder = new();

    public bool IsDisposed { get; private set; }

    public new SqlParameterCollection Parameters => _parameterHolder.Parameters;

    public DbTransaction? AssignedTransaction => DbTransaction;

    [AllowNull]
    public override string CommandText { get; set; } = "";

    public override int CommandTimeout { get; set; } = 30;

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; } = connection;

    protected override DbParameterCollection DbParameterCollection => _parameterHolder.Parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => throw new NotSupportedException();

    public override object? ExecuteScalar() => connection.Scalar;

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => new SqlParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => connection.NextRows().CreateDataReader();

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        if (disposing)
        {
            _parameterHolder.Dispose();
        }

        base.Dispose(disposing);
    }
}
