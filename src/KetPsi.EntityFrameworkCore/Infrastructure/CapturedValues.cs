using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Reads the value of a constant or captured variable from a predicate's expression tree. Generated interceptors
/// navigate to the exact node; this resolves only the node kinds the C# compiler uses for such values.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapturedValues
{
    private const string ImplicitOperator = "op_Implicit";

    /// <summary>Reads the value of <paramref name="node"/>.</summary>
    /// <exception cref="NotSupportedException">The node is not a constant, a field or property chain, or a conversion of one.</exception>
    public static object? Read(Expression node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ConstantExpression constant:
                return constant.Value;

            // Lifting to a nullable type, enum comparisons and widening insert conversions; the value is the operand.
            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null } conversion:
                return Read(conversion.Operand);

            case MemberExpression member:
                var target = member.Expression is null ? null : Read(member.Expression);
                return member.Member switch
                {
                    FieldInfo field => field.GetValue(target),
                    PropertyInfo property => property.GetValue(target),
                    _ => throw Unsupported(node),
                };

            // C# 14 converts arrays to spans for MemoryExtensions.Contains; the value is the array.
            case MethodCallExpression { Method.Name: ImplicitOperator, Arguments.Count: 1 } call when IsSpan(call.Method.DeclaringType):
                return Read(call.Arguments[0]);

            default:
                throw Unsupported(node);
        }
    }

    /// <summary>
    /// Reads every captured value of a <c>Select</c> lambda, in source order: each captured variable, field of the
    /// enclosing instance and static field or property, as the compiler stores it in the tree. Constants are not
    /// included; the generated reader writes them itself.
    /// </summary>
    public static object?[] ReadAll(LambdaExpression lambda)
    {
        ArgumentNullException.ThrowIfNull(lambda);

        var collector = new Collector();
        collector.Visit(lambda.Body);
        return [.. collector.Values];
    }

    private static bool IsSpan(Type? type)
        => type is { IsGenericType: true } && (type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) || type.GetGenericTypeDefinition() == typeof(Span<>));

    private static NotSupportedException Unsupported(Expression node)
        => new(string.Format(CultureInfo.InvariantCulture, ExceptionMessages.CapturedValueNotSupported, node.NodeType, node));

    // The visitor walks the tree in source order. A captured value is a member read from a closure or the enclosing
    // instance, which the compiler stores as constants, or a static member; members read from it are left to the reader.
    private sealed class Collector : ExpressionVisitor
    {
        public List<object?> Values { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is null or ConstantExpression)
            {
                Values.Add(Read(node));
                return node;
            }

            return base.VisitMember(node);
        }
    }
}
