namespace KetPsi.EntityFrameworkCore.SqlServer;

internal static class ExceptionMessages
{
    public const string UnsupportedListValueType =
        "Values of type '{0}' cannot be passed in a SQL Server Contains list.";

    public const string SumNeedsNumericType =
        "Sum and Average work in int, long, decimal, double or float.";

    public const string FunctionArgumentCount =
        "{0} takes {1} argument(s).";

    public const string CastTypeNotSupported =
        "SQL Server has no store type KetPsi.EntityFrameworkCore converts '{0}' to.";

    public const string TupleColumnsMismatch =
        "A tuple list needs at least one column, and one store type per column.";
}
