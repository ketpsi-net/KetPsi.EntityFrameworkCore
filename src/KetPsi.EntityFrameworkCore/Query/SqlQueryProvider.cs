using System.Globalization;
using System.Linq.Expressions;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// Provider of <see cref="SqlQueryable{T}"/>. Every intercepted operator bypasses it, so any
/// call that reaches it is an operator the generator did not intercept, and it throws.
/// </summary>
internal sealed class SqlQueryProvider : IQueryProvider
{
    public static SqlQueryProvider Instance { get; } = new();

    private SqlQueryProvider()
    {
    }

    public IQueryable CreateQuery(Expression expression) => throw NotIntercepted(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => throw NotIntercepted(expression);

    public object? Execute(Expression expression) => throw NotIntercepted(expression);

    public TResult Execute<TResult>(Expression expression) => throw NotIntercepted(expression);

    private static NotSupportedException NotIntercepted(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var operatorName = expression is MethodCallExpression call
            ? call.Method.Name
            : expression.NodeType.ToString();

        return new NotSupportedException(
            string.Format(CultureInfo.InvariantCulture, ExceptionMessages.OperatorNotIntercepted, operatorName));
    }
}
