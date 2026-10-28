namespace KetPsi.EntityFrameworkCore.SqlServer;

internal static class ExceptionMessages
{
    public const string UnsupportedListValueType =
        "Values of type '{0}' cannot be passed in a SQL Server Contains list.";

    public const string TupleColumnsMismatch =
        "A tuple list needs at least one column, and one store type per column.";
}
