namespace KetPsi.EntityFrameworkCore;

internal static class ExceptionMessages
{
    public const string OperatorNotIntercepted =
        "The query operator '{0}' was not intercepted by KetPsi.EntityFrameworkCore. Every operator on an entity query " +
        "must be translated at build time; make sure the project containing this call references " +
        "KetPsi.EntityFrameworkCore.Generator and that the build reported no KP diagnostics.";

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

    public const string NavigationNotFound =
        "The entity type '{0}' has no navigation '{1}'.";

    public const string ManyToManyNotSupported =
        "The navigation '{0}.{1}' is many-to-many, which KetPsi.EntityFrameworkCore does not support yet. Map the join " +
        "entity type and load it with Include(...).ThenInclude(...).";

    public const string CollectionLoadRequiresCollection =
        "Only a collection navigation is loaded with its own statement; references are joined.";

    public const string CollectionLoadRequiresKeys =
        "A collection is only loaded for at least one parent key.";

    public const string PropertyNotBoolean =
        "The property '{0}.{1}' is used as a boolean predicate, but its type is '{2}'.";

    public const string RowLimitRequiresRows =
        "A row limit only applies to statements that return entity rows.";

    public const string RootEntityMismatch =
        "The query reads '{0}', but the statement is being built for '{1}'.";

    public const string ContainsListNotEnumerable =
        "The list passed to Contains for property '{0}' must be a collection, but it was '{1}'.";

    public const string NotADbContextType =
        "The type '{0}' does not derive from DbContext.";

    public const string QueryFilterAlreadyRegistered =
        "A different translation of the query filter '{1}' on '{0}' is already registered for '{2}'.";

    public const string QueryFilterNotTranslated =
        "The EF Core model declares the query filter '{1}' on '{0}', but KetPsi.EntityFrameworkCore has no build-time " +
        "translation of it for '{2}'. Configure the filter with HasQueryFilter in code KetPsi.EntityFrameworkCore.Generator " +
        "can see, or use IgnoreQueryFilters.";

    public const string QueryFilterValueCountMismatch =
        "The query filter '{1}' on '{0}' reads {2} value(s), but its value reader returned {3}.";

    public const string EntityReaderNotRegistered =
        "No generated reader is registered for the entity type '{0}'. Make sure the project that declares the query " +
        "references KetPsi.EntityFrameworkCore.Generator.";

    public const string AggregateNeedsOneColumn =
        "An aggregate without a selector works on a query projected to one property, such as Select(x => x.Price).Sum().";

    public const string ProjectionRequiresColumns =
        "A projection reads at least one entity property or entity.";

    public const string CollectionNavigationInProjection =
        "KetPsi.EntityFrameworkCore cannot project the collection navigation '{1}' of '{0}' yet; include it on a projected entity instead.";

    public const string AggregateNeedsColumnOnly =
        "An aggregate works on one column, but the projection selects an entity.";

    public const string NotAProjectedQuery =
        "KetPsi.EntityFrameworkCore can only continue a query projected by an intercepted Select, but the source is of type '{0}'.";

    public const string NotAGroupedQuery =
        "KetPsi.EntityFrameworkCore can only continue a query grouped by an intercepted GroupBy, but the source is of type '{0}'.";

    public const string GroupEnumerationNotSupported =
        "The groups of a GroupBy are read through their key and element readers; a statement for grouped rows needs a Select.";

    public const string GroupRowsAfterGroupOperators =
        "KetPsi.EntityFrameworkCore can only return the groups of a GroupBy directly; after Where or OrderBy on the " +
        "groups, select their keys and aggregates with Select.";

    public const string GroupAggregateThroughNavigation =
        "An aggregate of a group must read a property of the grouped entity itself, not one through a navigation.";

    public const string GroupAggregateNeedsNumericType =
        "Sum and Average of a group need the numeric type of their overload.";

    public const string GroupKeyRequired =
        "A GroupBy needs at least one key path.";

    public const string GroupKeyIndexOutOfRange =
        "The group value reads key part {0}, but the GroupBy has {1} key part(s).";

    public const string GroupValuesRequired =
        "A Select on groups needs at least one value.";

    public const string HavingOperandOutOfRange =
        "The group predicate reads operand '{0}', but there are {1} operand(s).";

    // EF Core's own message, so no-tracking queries with a cycle fail the same way.
    public const string IncludeWithCycle =
        "The Include path '{0}->{1}' results in a cycle. Cycles are not allowed in no-tracking queries; either use a " +
        "tracking query or remove the cycle.";

    public const string ThenIncludeRequiresInclude =
        "ThenInclude continues the path of the Include or ThenInclude right before it.";

    public const string KeyReaderRequiresKey =
        "The entity type has no primary key to read.";

    public const string NavigationAccessorNotRegistered =
        "No generated accessor is registered for the navigation '{0}.{1}'. Make sure the project that declares the query " +
        "references KetPsi.EntityFrameworkCore.Generator.";

    public const string TrackingRequiresEfCore9UnderAot =
        "Tracking queries need EF Core 9 or later under Native AOT; EF Core 8 cannot track entities there. Add " +
        "AsNoTracking() to the query, or set the context's QueryTrackingBehavior to NoTracking.";

    // Same messages as LINQ and EF Core, so callers see the same exceptions.
    public const string SequenceContainsNoElements = "Sequence contains no elements";

    public const string SequenceContainsMoreThanOneElement = "Sequence contains more than one element";

    // EF Core's messages for Find, with its parameter names in order.
    public const string FindKeyless =
        "The invoked method cannot be used for the entity type '{0}' because it does not have a primary key. For more information on keyless entity types, see https://go.microsoft.com/fwlink/?linkid=2141943.";

    public const string FindNotCompositeKey =
        "Entity type '{0}' is defined with a single key property, but {1} values were passed to the 'Find' method.";

    public const string FindValueCountMismatch =
        "Entity type '{0}' is defined with a {1}-part composite key, but {2} values were passed to the 'Find' method.";

    public const string FindValueTypeMismatch =
        "The key value at position {0} of the call to 'DbSet<{1}>.Find' was of type '{2}', which does not match the property type of '{3}'.";

    public const string LastWithoutOrdering =
        "Queries performing '{0}' operation must have a deterministic sort order. Rewrite the query to apply an 'OrderBy' operation on the sequence before calling '{0}'.";

    // EF Core's async terminals throw their own messages, which end with a period.
    public const string SequenceContainsNoElementsAsync = "Sequence contains no elements.";

    public const string SequenceContainsMoreThanOneElementAsync = "Sequence contains more than one element.";

    // What EF Core 10 throws for Min, Max or Average of no rows into a non-nullable type.
    public const string NullableObjectMustHaveAValue = "Nullable object must have a value.";

    public const string CapturedValueNotSupported =
        "The predicate value '{1}' ({0}) cannot be read. The generated interceptor does not match the expression tree " +
        "the compiler built; please report this with the query.";

    public const string DialectAlreadyRegistered =
        "The EF Core provider '{0}' already has the SQL dialect '{1}' registered; '{2}' cannot replace it.";

    public const string DialectNotRegistered =
        "No KetPsi.EntityFrameworkCore SQL dialect is registered for the EF Core provider '{0}'. " +
        "Reference the dialect package for your database, such as KetPsi.EntityFrameworkCore.SqlServer.";
}
