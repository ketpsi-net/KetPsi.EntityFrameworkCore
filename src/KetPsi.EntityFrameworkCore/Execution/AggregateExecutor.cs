using System.Globalization;
using KetPsi.EntityFrameworkCore.Query;
using KetPsi.EntityFrameworkCore.Sql;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>Executes <c>Sum</c>, <c>Average</c>, <c>Min</c> and <c>Max</c>, with EF Core's results and exceptions.</summary>
internal static class AggregateExecutor
{
    // EF Core 10 reads an empty aggregate as a null it cannot unwrap; earlier versions report the empty sequence.
    private const int EfCoreMajorWithNullableMessage = 10;

    public static TResult Execute<TResult>(DbContext context, QueryNode tail, SqlAggregate aggregate)
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(context, tail, QueryTerminal.Aggregate, rowLimit: null, aggregate);
        return Convert<TResult>(QueryExecutor.ExecuteScalar(context, dialect, statement), QueryRunner.EfCoreMajorVersion);
    }

    public static async Task<TResult> ExecuteAsync<TResult>(
        DbContext context, QueryNode tail, SqlAggregate aggregate, CancellationToken cancellationToken)
    {
        var (_, dialect, statement) = QueryRunner.PrepareStatement(context, tail, QueryTerminal.Aggregate, rowLimit: null, aggregate);
        var value = await QueryExecutor.ExecuteScalarAsync(context, dialect, statement, cancellationToken).ConfigureAwait(false);
        return Convert<TResult>(value, QueryRunner.EfCoreMajorVersion);
    }

    /// <summary>
    /// Converts the aggregate's provider value to the operator's result type. SQL returns null for an aggregate of no
    /// rows; that is <see langword="null"/> for a nullable or reference result, and an exception for a value type,
    /// as in EF Core. <c>Sum</c> never returns null, because its SQL coalesces to zero.
    /// </summary>
    internal static TResult Convert<TResult>(object? value, int efCoreMajorVersion)
    {
        var type = typeof(TResult);
        var underlying = Nullable.GetUnderlyingType(type);
        if (value is null or DBNull)
        {
            return !type.IsValueType || underlying is not null
                ? default!
                : throw new InvalidOperationException(efCoreMajorVersion >= EfCoreMajorWithNullableMessage
                    ? ExceptionMessages.NullableObjectMustHaveAValue
                    : ExceptionMessages.SequenceContainsNoElementsAsync);
        }

        var target = underlying ?? type;
        var converted = value.GetType() == target ? value : ConvertTo(value, target);
        return (TResult)converted;
    }

    private static object ConvertTo(object value, Type target)
    {
        if (target.IsEnum)
        {
            return Enum.ToObject(target, value);
        }

        return (target, value) switch
        {
            _ when target == typeof(DateOnly) && value is DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ when target == typeof(TimeOnly) && value is TimeSpan time => TimeOnly.FromTimeSpan(time),
            _ => System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture),
        };
    }
}
