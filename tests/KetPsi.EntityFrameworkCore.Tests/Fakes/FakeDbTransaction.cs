using System.Data;
using System.Data.Common;

namespace KetPsi.EntityFrameworkCore.Tests.Fakes;

public sealed class FakeDbTransaction(FakeDbConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
    public override IsolationLevel IsolationLevel => isolationLevel;

    protected override DbConnection DbConnection => connection;

    public override void Commit()
    {
    }

    public override void Rollback()
    {
    }
}
