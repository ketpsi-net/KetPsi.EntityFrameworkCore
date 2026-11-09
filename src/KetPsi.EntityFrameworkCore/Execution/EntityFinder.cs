using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure.Templates;
using KetPsi.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Execution;

/// <summary>
/// <c>Find</c> and <c>FindAsync</c>, as EF Core runs them: key values with a <see langword="null"/> find nothing, an
/// entity the context already tracks under the key is returned without a query (even when it is added or deleted), and
/// otherwise the entity is queried by its key like <c>FirstOrDefault</c>, with the context's default tracking and the
/// query filters. Wrong key values throw EF Core's <see cref="ArgumentException"/> before anything else happens.
/// </summary>
internal static class EntityFinder
{
    // One predicate per entity, so each binds once like a generated one.
    private static readonly ConditionalWeakTable<BoundEntity, PredicateTemplate> KeyPredicates = [];

    public static T? Find<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(DbSet<T> set, object?[]? keyValues)
        where T : class
    {
        var (query, tracked) = Prepare(set, keyValues);
        return query is null ? tracked : QueryRunner.FirstOrDefault(query);
    }

    public static ValueTask<T?> FindAsync<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        DbSet<T> set, object?[]? keyValues, CancellationToken cancellationToken)
        where T : class
    {
        // Like EF Core, wrong key values throw when FindAsync is called, not when it is awaited.
        var (query, tracked) = Prepare(set, keyValues);
        return query is null ? new ValueTask<T?>(tracked) : new ValueTask<T?>(QueryRunner.FirstOrDefaultAsync(query, cancellationToken));
    }

    /// <summary>The query to run, or <see langword="null"/> with the result when no query is needed.</summary>
    private static (SqlQueryable<T>? Query, T? Tracked) Prepare<[DynamicallyAccessedMembers(EntityMembers.Required)] T>(
        DbSet<T> set, object?[]? keyValues)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(set);
        if (keyValues is null || Array.Exists(keyValues, static value => value is null))
        {
            return (null, null);
        }

        var context = set.GetService<ICurrentDbContext>().Context;
        var entity = QueryRunner.Binder(context)(typeof(T));
        var key = entity.PrimaryKey ?? throw new InvalidOperationException(
            string.Format(CultureInfo.InvariantCulture, ExceptionMessages.FindKeyless, DisplayName(typeof(T))));
        Validate(key, keyValues, typeof(T));

        if (context.Set<T>().Local.FindEntryUntyped(keyValues) is { } entry)
        {
            return (null, entry.Entity);
        }

        var predicate = KeyPredicates.GetValue(entity, static bound => KeyPredicate(bound.PrimaryKey!));
        return (new SqlQueryable<T>(context, new WhereNode(new RootNode(typeof(T)), predicate, [.. keyValues])), null);
    }

    private static void Validate(IReadOnlyList<BoundProperty> key, object?[] keyValues, Type entityType)
    {
        if (keyValues.Length != key.Count)
        {
            throw new ArgumentException(key.Count == 1
                ? string.Format(CultureInfo.InvariantCulture, ExceptionMessages.FindNotCompositeKey, DisplayName(entityType), keyValues.Length)
                : string.Format(CultureInfo.InvariantCulture, ExceptionMessages.FindValueCountMismatch, DisplayName(entityType), key.Count, keyValues.Length));
        }

        for (var i = 0; i < key.Count; i++)
        {
            var valueType = keyValues[i]!.GetType();
            var propertyType = key[i].ClrType;
            if (valueType != (Nullable.GetUnderlyingType(propertyType) ?? propertyType))
            {
                throw new ArgumentException(string.Format(
                    CultureInfo.InvariantCulture, ExceptionMessages.FindValueTypeMismatch, i, DisplayName(entityType), DisplayName(valueType), DisplayName(propertyType)));
            }
        }
    }

    // key[0] == value0 && key[1] == value1 ...
    private static PredicateTemplate KeyPredicate(IReadOnlyList<BoundProperty> key)
    {
        PredicateTemplate predicate = new ComparisonTemplate(key[0].Name, ComparisonOperator.Equal, 0);
        for (var i = 1; i < key.Count; i++)
        {
            predicate = new AndTemplate(predicate, new ComparisonTemplate(key[i].Name, ComparisonOperator.Equal, i));
        }

        return predicate;
    }

    /// <summary>A type's name as EF Core writes it in messages: C# keywords for built-in types, <c>?</c> for nullable ones.</summary>
    internal static string DisplayName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return DisplayName(underlying) + "?";
        }

        if (type.IsEnum)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return DisplayName(type.GetElementType()!) + "[]";
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Char => "char",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.String => "string",
            _ when type == typeof(object) => "object",
            _ when type.IsGenericType => type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]
                + "<" + string.Join(", ", type.GetGenericArguments().Select(DisplayName)) + ">",
            _ => type.Name,
        };
    }
}
