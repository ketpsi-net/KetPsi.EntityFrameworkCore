namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>An entity property bound to its column.</summary>
/// <param name="Name">The property name.</param>
/// <param name="ColumnName">The unquoted column name.</param>
/// <param name="StoreType">The column's store type, such as <c>nvarchar(100)</c>.</param>
/// <param name="IsNullable">Whether the model allows the column to hold <see langword="null"/>.</param>
/// <param name="ClrType">The property's CLR type.</param>
internal sealed record BoundProperty(string Name, string ColumnName, string StoreType, bool IsNullable, Type ClrType);
