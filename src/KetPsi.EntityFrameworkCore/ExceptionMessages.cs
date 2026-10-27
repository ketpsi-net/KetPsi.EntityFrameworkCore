namespace KetPsi.EntityFrameworkCore;

internal static class ExceptionMessages
{
    public const string OperatorNotIntercepted =
        "The query operator '{0}' was not intercepted by KetPsi.EntityFrameworkCore. Every operator on an entity query " +
        "must be translated at build time; make sure the project containing this call references " +
        "KetPsi.EntityFrameworkCore.Generator and that the build reported no KP diagnostics.";

    public const string EnumerationNotSupported =
        "Enumerating a KetPsi.EntityFrameworkCore query directly is not supported yet. Use a terminal operator such as ToList.";

    public const string UninterceptedEntityFrameworkOperator =
        "The query was composed by an Entity Framework Core operator that KetPsi.EntityFrameworkCore did not intercept, " +
        "so it would run through EF Core's runtime query pipeline. Remove the operator or replace it with a supported one.";

    public const string NotAnEntityQuery =
        "KetPsi.EntityFrameworkCore can only compose queries rooted at a DbSet, but the source is of type '{0}'.";

    public const string ValueCountMismatch =
        "The predicate reads {0} value(s), but {1} were supplied.";

    public const string EntityNotInModel =
        "The type '{0}' is not an entity type in the EF Core model.";

    public const string EntityNotMappedToTable =
        "The entity type '{0}' is not mapped to a table. KetPsi.EntityFrameworkCore only queries entities mapped to a table.";

    public const string EntityInheritanceNotSupported =
        "The entity type '{0}' is part of an inheritance hierarchy, which KetPsi.EntityFrameworkCore does not support yet.";

    public const string OwnedEntityNotSupported =
        "The entity type '{0}' is owned, or owns other entity types, which KetPsi.EntityFrameworkCore does not support yet.";

    public const string ComplexPropertyNotSupported =
        "The entity type '{0}' has complex properties, which KetPsi.EntityFrameworkCore does not support yet.";

    public const string ValueConverterNotSupported =
        "The property '{0}.{1}' has a value converter, which KetPsi.EntityFrameworkCore does not support yet.";

    public const string PropertyColumnMissing =
        "The property '{0}.{1}' has no column in table '{2}'.";

    public const string PropertyNotMapped =
        "The entity type '{0}' has no mapped property '{1}'. The property may be ignored in the EF Core model.";

    public const string PropertyNotBoolean =
        "The property '{0}.{1}' is used as a boolean predicate, but its type is '{2}'.";

    public const string DialectAlreadyRegistered =
        "The EF Core provider '{0}' already has the SQL dialect '{1}' registered; '{2}' cannot replace it.";

    public const string DialectNotRegistered =
        "No KetPsi.EntityFrameworkCore SQL dialect is registered for the EF Core provider '{0}'. " +
        "Reference the dialect package for your database, such as KetPsi.EntityFrameworkCore.SqlServer.";
}
