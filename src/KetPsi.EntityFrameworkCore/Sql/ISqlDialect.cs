namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>
/// Database-specific SQL syntax. The SQL builder composes statements from these fragments,
/// so one implementation per database provider is all a new database needs.
/// </summary>
public interface ISqlDialect
{
    /// <summary>The EF Core provider name (<c>DatabaseFacade.ProviderName</c>) this dialect serves.</summary>
    string ProviderName { get; }

    /// <summary>The literal for boolean <see langword="true"/>.</summary>
    string TrueLiteral { get; }

    /// <summary>The literal for boolean <see langword="false"/>.</summary>
    string FalseLiteral { get; }

    /// <summary>Quotes an identifier, escaping any quote characters inside it.</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>Returns the quoted table name, qualified with the schema when there is one.</summary>
    string QualifyTable(string table, string? schema);

    /// <summary>Returns the placeholder for the parameter at <paramref name="index"/>.</summary>
    string ParameterName(int index);

    /// <summary>Returns the row-count aggregate: <c>Count</c> or, when <paramref name="isLong"/>, <c>LongCount</c>.</summary>
    string Count(bool isLong);

    /// <summary>Wraps <paramref name="subquery"/> in a statement that returns one boolean: whether it has any row.</summary>
    string Exists(string subquery);

    /// <summary>Returns the clauses that apply <c>Skip</c> and <c>Take</c>.</summary>
    /// <param name="offsetParameter">The parameter holding the <c>Skip</c> count, or <see langword="null"/>.</param>
    /// <param name="limitParameter">The parameter holding the <c>Take</c> count, or <see langword="null"/>.</param>
    /// <param name="hasOrderBy">Whether the statement already has an <c>ORDER BY</c>.</param>
    SqlPaging Paging(string? offsetParameter, string? limitParameter, bool hasOrderBy);

    /// <summary>
    /// Returns a predicate that is true when <paramref name="column"/> equals a value in the list
    /// passed as <paramref name="listParameter"/>.
    /// </summary>
    /// <param name="column">The quoted column.</param>
    /// <param name="listParameter">The parameter holding the value from <see cref="CreateListParameterValue"/>.</param>
    /// <param name="storeType">The column's store type, used to type the list elements.</param>
    string InList(string column, string listParameter, string storeType);

    /// <summary>Converts the values of a <c>Contains</c> list into the single parameter value <see cref="InList"/> expects.</summary>
    /// <exception cref="NotSupportedException">A value has a type the dialect cannot pass in a list.</exception>
    object CreateListParameterValue(IEnumerable<object?> values);
}
