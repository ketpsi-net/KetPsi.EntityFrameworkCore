using System.Linq.Expressions;

namespace KetPsi.EntityFrameworkCore;

/// <summary>Query operators added by KetPsi.EntityFrameworkCore.</summary>
public static class KetPsiQueryableExtensions
{
    /// <summary>
    /// Filters the query with <paramref name="predicate"/> only when <paramref name="condition"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// KetPsi.EntityFrameworkCore.Generator translates the predicate at build time. Without the generator, this
    /// is <c>condition ? source.Where(predicate) : source</c>, so queries keep working on plain EF Core.
    /// </remarks>
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> source, bool condition, Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        return condition ? source.Where(predicate) : source;
    }
}
