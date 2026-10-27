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
}
