using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace KetPsi.EntityFrameworkCore.Query;

/// <summary>
/// The query <c>Include</c> and <c>ThenInclude</c> return: EF Core's signatures return
/// <see cref="IIncludableQueryable{TEntity, TProperty}"/>, which the next <c>ThenInclude</c> continues from.
/// </summary>
internal sealed class IncludableSqlQueryable<[DynamicallyAccessedMembers(EntityMembers.Required)] TEntity, TProperty>(
    DbContext context, IncludeNode tail)
    : SqlQueryable<TEntity>(context, tail), IIncludableQueryable<TEntity, TProperty>
    where TEntity : class;
