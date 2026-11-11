using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KetPsi.EntityFrameworkCore.Binding;

/// <summary>An entity property bound to its column.</summary>
/// <param name="Name">The property name.</param>
/// <param name="ColumnName">The unquoted column name.</param>
/// <param name="StoreType">The column's store type, such as <c>nvarchar(100)</c>.</param>
/// <param name="IsNullable">Whether the model allows the column to hold <see langword="null"/>.</param>
/// <param name="ClrType">The property's CLR type.</param>
/// <param name="IsShadow">
/// Whether the property exists only in the EF model, such as a foreign key without a CLR property. Generated readers
/// cannot set it, so tracked queries give its value to EF Core's change tracker instead.
/// </param>
/// <param name="Converter">
/// The value converter of the property's type mapping, which EF Core applies to every value sent to or read from the
/// column, such as an enum stored as a string or a strongly typed key stored as a number; <see langword="null"/> when
/// the column holds the CLR value itself, or an enum's underlying number.
/// </param>
internal sealed record BoundProperty(
    string Name, string ColumnName, string StoreType, bool IsNullable, Type ClrType, bool IsShadow, ValueConverter? Converter = null)
{
    /// <summary>The value the column holds for <paramref name="value"/>, as EF Core sends it.</summary>
    public object? ToProvider(object? value)
        => Converter is null || value is null && !Converter.ConvertsNulls ? value : Converter.ConvertToProvider(value);

    /// <summary>The CLR value for a value read from the column, as EF Core materializes it.</summary>
    public object? FromProvider(object? value)
        => Converter is null || value is null or DBNull && !Converter.ConvertsNulls
            ? value
            : Converter.ConvertFromProvider(value is DBNull ? null : value);
}
