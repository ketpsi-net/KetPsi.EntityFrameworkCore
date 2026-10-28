using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace KetPsi.EntityFrameworkCore.TestUtilities;

/// <summary>An in-memory connection that records the commands it creates and answers them with canned results.</summary>
public sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public List<FakeDbCommand> Commands { get; } = [];

    public FakeDbCommand LastCommand => Commands[^1];

    /// <summary>The rows every reader command returns once <see cref="RowSets"/> is empty.</summary>
    public DataTable Rows { get; set; } = new();

    /// <summary>Rows for the next reader commands, one table per command, in order.</summary>
    public Queue<DataTable> RowSets { get; } = new();

    internal DataTable NextRows() => RowSets.TryDequeue(out var rows) ? rows : Rows;

    /// <summary>The value every scalar command returns.</summary>
    public object? Scalar { get; set; }

    public int OpenCount { get; private set; }

    public int CloseCount { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = "Server=fake;Database=fake";

    public override string Database => "fake";

    public override string DataSource => "fake";

    public override string ServerVersion => "16.00.0000";

    public override ConnectionState State => _state;

    public override void Open()
    {
        OpenCount++;
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Open();
        return Task.CompletedTask;
    }

    public override void Close()
    {
        CloseCount++;
        _state = ConnectionState.Closed;
    }

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeDbTransaction(this, isolationLevel);

    protected override DbCommand CreateDbCommand()
    {
        var command = new FakeDbCommand(this);
        Commands.Add(command);
        return command;
    }
}
