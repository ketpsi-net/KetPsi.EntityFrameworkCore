using System.Data;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Sql;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.Fakes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

/// <summary>Properties with value converters, sent and read as EF Core does on SQL Server (probed on EF Core 9, 10 and 11).</summary>
public sealed class ValueConverterTests : IDisposable
{
    private readonly FakeDbConnection _connection = new();
    private readonly FakeTicketContext _db;

    public ValueConverterTests()
    {
        FakeTicketContext.EnsureRegistered();
        _db = new FakeTicketContext(_connection);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Bind_TakesTheConverterOfTheTypeMapping_ButNotOfAnEnumStoredAsItsNumber()
    {
        var entity = ModelBinder.GetEntity(_db.Model, typeof(Ticket));

        // HasConversion<string>() on an enum sets the converter on the type mapping only.
        Assert.Equal(typeof(string), entity.GetProperty(nameof(Ticket.State)).Converter!.ProviderClrType);
        Assert.Equal(typeof(int), entity.GetProperty(nameof(Ticket.Id)).Converter!.ProviderClrType);
        Assert.Equal(typeof(string), entity.GetProperty(nameof(Ticket.IsUrgent)).Converter!.ProviderClrType);
        Assert.Null(entity.GetProperty(nameof(Ticket.Kind)).Converter);
    }

    [Fact]
    public void Comparison_SendsTheStoredValue_TypedByTheColumn()
    {
        _connection.Rows = _db.Table<Ticket>();

        SqlQuery.ToList(Where(new ComparisonTemplate(nameof(Ticket.State), ComparisonOperator.Equal, 0), TicketState.Closed));

        Assert.EndsWith("WHERE [t0].[State] = @p0", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        var parameter = Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>());
        Assert.Equal("Closed", parameter.Value);
        Assert.Equal(SqlDbType.NVarChar, parameter.SqlDbType);
        Assert.Equal(20, parameter.Size);
    }

    // The compiler writes an enum constant into the expression tree as its number.
    [Fact]
    public void Comparison_WithAnEnumConstantsNumber_SendsTheStoredValue()
    {
        _connection.Rows = _db.Table<Ticket>();

        SqlQuery.ToList(Where(new ComparisonTemplate(nameof(Ticket.State), ComparisonOperator.NotEqual, 0), (int)TicketState.Closed));

        Assert.Equal("Closed", Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>()).Value);
    }

    [Fact]
    public void Comparison_StronglyTypedKey_SendsItsNumber()
    {
        _connection.Rows = _db.Table<Ticket>();

        SqlQuery.ToList(Where(new ComparisonTemplate(nameof(Ticket.Id), ComparisonOperator.GreaterThan, 0), new TicketId(2)));

        Assert.Equal(2, Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>()).Value);
    }

    [Fact]
    public void Comparison_WithNull_IsANullCheck()
    {
        _connection.Rows = _db.Table<Ticket>();

        SqlQuery.ToList(Where(new ComparisonTemplate(nameof(Ticket.Points), ComparisonOperator.Equal, 0), [null]));

        Assert.EndsWith("WHERE [t0].[Points] IS NULL", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.Empty(_connection.LastCommand.Parameters);
    }

    [Theory]
    [InlineData(false, "Y")]
    [InlineData(true, "N")]
    public void BoolStoredAsText_IsComparedWithTheStoredValue(bool negated, string stored)
    {
        _connection.Rows = _db.Table<Ticket>();
        PredicateTemplate urgent = new BooleanPropertyTemplate(nameof(Ticket.IsUrgent));

        SqlQuery.ToList(Where(negated ? new NotTemplate(urgent) : urgent));

        Assert.EndsWith("WHERE [t0].[IsUrgent] = @p0", _connection.LastCommand.CommandText, StringComparison.Ordinal);
        Assert.Equal(stored, Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>()).Value);
    }

    [Fact]
    public void Contains_SendsTheStoredValues()
    {
        _connection.Rows = _db.Table<Ticket>();

        SqlQuery.ToList(Where(
            new AndTemplate(new InListTemplate(nameof(Ticket.State), 0), new InListTemplate(nameof(Ticket.Id), 1)),
            new[] { TicketState.Open, TicketState.Closed },
            new List<TicketId> { new(1), new(3) }));

        var parameters = _connection.LastCommand.Parameters.Cast<SqlParameter>().ToList();
        Assert.Equal("[\"Open\",\"Closed\"]", parameters[0].Value);
        Assert.Equal("[1,3]", parameters[1].Value);
        Assert.Contains("WITH ([value] nvarchar(20) '$')", _connection.LastCommand.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_AreReadThroughTheConverters()
    {
        _connection.Rows = _db.Table<Ticket>(
            Row(1, "ABC", "Y", (int)TicketKind.Task, "13", "Open", "Closed"),
            Row(2, "XY", "N", (int)TicketKind.Bug, null, null, "Open"));

        var tickets = SqlQuery.ToList(SqlQuery.AsNoTracking(_db.Tickets));

        Assert.Equal(new TicketId(1), tickets[0].Id);
        Assert.Equal("abc", tickets[0].Code);
        Assert.True(tickets[0].IsUrgent);
        Assert.Equal(TicketKind.Task, tickets[0].Kind);
        Assert.Equal(13, tickets[0].Points);
        Assert.Equal(TicketState.Open, tickets[0].Previous);
        Assert.Equal(TicketState.Closed, tickets[0].State);
        Assert.False(tickets[1].IsUrgent);
        Assert.Null(tickets[1].Points);
        Assert.Null(tickets[1].Previous);
    }

    [Fact]
    public void NullInAConvertedColumnOfANonNullableProperty_ThrowsTheProvidersError()
    {
        _connection.Rows = _db.Table<Ticket>(Row(1, "ABC", "Y", 0, null, null, null));

        // EF Core reads the stored type first, so the provider reports the null; a DataTable reports it as a cast.
        Assert.Throws<InvalidCastException>(() => SqlQuery.ToList(SqlQuery.AsNoTracking(_db.Tickets)));
    }

    [Fact]
    public void Tracking_KeysTheEntityByItsConvertedKey()
    {
        _connection.Rows = _db.Table<Ticket>(Row(2, "ABC", "Y", 0, null, null, "Open"));

        var ticket = SqlQuery.Find(_db.Tickets, [new TicketId(2)])!;

        Assert.Equal(2, Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>()).Value);
        Assert.Equal(EntityState.Unchanged, _db.Entry(ticket).State);
        Assert.Same(ticket, SqlQuery.Find(_db.Tickets, [new TicketId(2)]));
        Assert.Single(_connection.Commands);

        // A second query returns the tracked instance for the row's converted key.
        Assert.Same(ticket, SqlQuery.Single(_db.Tickets));
    }

    [Fact]
    public void Projection_ReadsColumnsThroughTheConverters()
    {
        _connection.Rows = ProjectionTable(("State", typeof(string), "Closed"), ("Id", typeof(int), 4));

        var row = SqlProjection.Single(SqlQuery.Select(
            _db.Tickets,
            [nameof(Ticket.State), nameof(Ticket.Id)],
            [],
            static (reader, ordinals, _) => ((TicketState)reader.GetInt32(ordinals[0]), reader.GetFieldValue<TicketId>(ordinals[1]))));

        Assert.Equal((TicketState.Closed, new TicketId(4)), row);
    }

    [Fact]
    public void MinAndMax_ReadTheStoredValueThroughTheConverter()
    {
        _connection.Scalar = "Closed";
        Assert.Equal(
            TicketState.Closed,
            SqlQuery.Aggregate<Ticket, TicketState>(_db.Tickets, AggregateFunction.Max, nameof(Ticket.State), AggregateValueType.Column));

        _connection.Scalar = "9";
        Assert.Equal(9, SqlQuery.Aggregate<Ticket, int?>(_db.Tickets, AggregateFunction.Min, nameof(Ticket.Points), AggregateValueType.Column));

        _connection.Scalar = DBNull.Value;
        Assert.Null(SqlQuery.Aggregate<Ticket, int?>(_db.Tickets, AggregateFunction.Max, nameof(Ticket.Points), AggregateValueType.Column));
    }

    [Fact]
    public void IncludedCollection_MatchesTheParentsStoredKeys()
    {
        _connection.RowSets.Enqueue(_db.Table<Ticket>(Row(1, "ABC", "Y", 0, null, null, "Open")));
        _connection.RowSets.Enqueue(_db.Table<Note>(
            new Dictionary<string, object?> { [nameof(Note.Id)] = 10, [nameof(Note.Text)] = "x", [nameof(Note.TicketId)] = 1 }));

        var ticket = SqlQuery.Single(SqlQuery.Include<Ticket, List<Note>>(_db.Tickets, nameof(Ticket.Notes)));

        Assert.Equal("[1]", Assert.Single(_connection.LastCommand.Parameters.Cast<SqlParameter>()).Value);
        var note = Assert.Single(ticket.Notes);
        Assert.Equal(new TicketId(1), note.TicketId);
        Assert.Same(ticket, note.Ticket);
    }

    [Fact]
    public void FromSqlAsWritten_ReadsColumnsByNameThroughTheConverters()
    {
        var table = new DataTable();
        foreach (var (name, type) in new[] { ("State", typeof(string)), ("Points", typeof(string)), ("Previous", typeof(string)), ("Kind", typeof(int)), ("IsUrgent", typeof(string)), ("Code", typeof(string)), ("Id", typeof(int)) })
        {
            table.Columns.Add(name, type).AllowDBNull = true;
        }

        table.Rows.Add("Closed", "5", DBNull.Value, 1, "Y", "AB", 7);
        _connection.Rows = table;

        // ToList sends the SQL as written; Single would compose over it with TOP(2).
        var ticket = Assert.Single(SqlQuery.ToList(SqlQuery.AsNoTracking(SqlQuery.FromSql(_db.Tickets, $"SELECT * FROM Tickets"))));

        Assert.Equal((new TicketId(7), TicketState.Closed, 5, "ab", true), (ticket.Id, ticket.State, ticket.Points, ticket.Code, ticket.IsUrgent));
    }

    private IQueryable<Ticket> Where(PredicateTemplate predicate, params object?[] values)
        => SqlQuery.AsNoTracking(SqlQuery.Where(_db.Tickets, predicate, values));

    private static Dictionary<string, object?> Row(int id, string code, string urgent, int kind, string? points, string? previous, string? state)
        => new()
        {
            [nameof(Ticket.Id)] = id,
            [nameof(Ticket.Code)] = code,
            [nameof(Ticket.IsUrgent)] = urgent,
            [nameof(Ticket.Kind)] = kind,
            [nameof(Ticket.Points)] = points,
            [nameof(Ticket.Previous)] = previous,
            [nameof(Ticket.State)] = state,
        };

    private static DataTable ProjectionTable(params (string Name, Type Type, object Value)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type, _) in columns)
        {
            table.Columns.Add(name, type);
        }

        table.Rows.Add([.. columns.Select(static c => c.Value)]);
        return table;
    }
}
