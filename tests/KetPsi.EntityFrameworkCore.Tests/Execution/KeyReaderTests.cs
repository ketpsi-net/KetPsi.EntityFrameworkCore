using System.Data;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Execution;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Execution;

public sealed class KeyReaderTests
{
    private enum Priority
    {
        Low,
        High,
    }

    [Fact]
    public void Read_CompositeKey_ReturnsValuesInKeyOrder()
    {
        using var db = new CompositeKeyContext();
        var entity = ModelBinder.GetEntity(db.Model, typeof(Enrollment));
        var values = new Dictionary<string, object> { ["StudentId"] = 3, ["CourseId"] = 5, ["Grade"] = 90 };
        using var reader = Row(entity, values);

        Assert.Equal([3, 5], new KeyReader(entity).Read(reader));
    }

    [Fact]
    public void Constructor_KeylessEntity_Throws()
    {
        using var db = new KeylessContext();
        var entity = ModelBinder.GetEntity(db.Model, typeof(Report));

        Assert.Throws<ArgumentNullException>(() => new KeyReader(entity));
    }

    [Fact]
    public void Ordinal_ReturnsIndexInBoundProperties()
    {
        using var db = new CompositeKeyContext();
        var entity = ModelBinder.GetEntity(db.Model, typeof(Enrollment));

        Assert.Equal(2, KeyReader.Ordinal(entity, entity.Properties[2]));
    }

    [Fact]
    public void Ordinal_PropertyOfAnotherEntity_Throws()
    {
        using var db = new CompositeKeyContext();
        using var other = new KeylessContext();
        var entity = ModelBinder.GetEntity(db.Model, typeof(Enrollment));
        var property = ModelBinder.GetEntity(other.Model, typeof(Report)).Properties[0];

        Assert.Throws<ArgumentException>(() => KeyReader.Ordinal(entity, property));
    }

    [Fact]
    public void ToClrValue_DbNull_ReturnsNull()
        => Assert.Null(KeyReader.ToClrValue(DBNull.Value, typeof(int?)));

    [Fact]
    public void ToClrValue_Enum_ConvertsFromProviderValue()
    {
        Assert.Equal(Priority.High, KeyReader.ToClrValue(1, typeof(Priority)));
        Assert.Equal(Priority.High, KeyReader.ToClrValue(1, typeof(Priority?)));
    }

    [Fact]
    public void ToClrValue_OtherValue_ReturnsItUnchanged()
        => Assert.Equal("alpha", KeyReader.ToClrValue("alpha", typeof(string)));

    [Fact]
    public void Comparer_EqualValues_AreEqualWithSameHash()
    {
        object?[] x = [1, "a", new byte[] { 1, 2 }];
        object?[] y = [1, "a", new byte[] { 1, 2 }];

        Assert.True(KeyReader.Comparer.Equals(x, y));
        Assert.Equal(KeyReader.Comparer.GetHashCode(x), KeyReader.Comparer.GetHashCode(y));
    }

    [Fact]
    public void Comparer_DifferentValues_AreNotEqual()
    {
        Assert.False(KeyReader.Comparer.Equals([1, 2], [2, 1]));
        Assert.False(KeyReader.Comparer.Equals([new byte[] { 1 }], [new byte[] { 2 }]));
        Assert.False(KeyReader.Comparer.Equals(["a"], ["A"]));
    }

    private static DataTableReader Row(BoundEntity entity, Dictionary<string, object> values)
    {
        var table = new DataTable();
        foreach (var property in entity.Properties)
        {
            table.Columns.Add(property.Name, values[property.Name].GetType());
        }

        table.Rows.Add([.. entity.Properties.Select(p => values[p.Name])]);
        var reader = table.CreateDataReader();
        reader.Read();
        return reader;
    }
}
