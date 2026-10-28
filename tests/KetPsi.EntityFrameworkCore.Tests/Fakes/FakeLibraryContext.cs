using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Infrastructure;
using KetPsi.EntityFrameworkCore.SqlServer;
using KetPsi.EntityFrameworkCore.TestUtilities;
using KetPsi.EntityFrameworkCore.Tests.TestModel;
using Microsoft.EntityFrameworkCore;

namespace KetPsi.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// The library model over a <see cref="FakeDbConnection"/>, with reflection-based stand-ins for the readers and
/// navigation accessors the generator emits. Tests run under JIT, so reflection is fine here.
/// </summary>
public sealed class FakeLibraryContext(FakeDbConnection connection, QueryTrackingBehavior tracking = QueryTrackingBehavior.TrackAll)
    : DbContext
{
    private static readonly Type[] EntityTypes = [typeof(Writer), typeof(WriterProfile), typeof(Book), typeof(Editor), typeof(Review)];

    public DbSet<Writer> Writers => Set<Writer>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<Review> Reviews => Set<Review>();

    public static void EnsureRegistered()
    {
        SqlDialects.Register(SqlServerDialect.Instance);
        EntityReaders.Register(new ReflectionReader<Writer>());
        EntityReaders.Register(new ReflectionReader<WriterProfile>());
        EntityReaders.Register(new ReflectionReader<Book>());
        EntityReaders.Register(new ReflectionReader<Editor>());
        EntityReaders.Register(new ReflectionReader<Review>());
        foreach (var type in EntityTypes)
        {
            foreach (var property in type.GetProperties())
            {
                if (!property.PropertyType.IsValueType && property.PropertyType != typeof(string))
                {
                    EntityNavigations.Register(new ReflectionNavigationAccessor(type, property));
                }
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer(connection).UseQueryTrackingBehavior(tracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => LibraryContext.Configure(modelBuilder);

    /// <summary>Reads every scalar CLR property by name, as a generated reader does.</summary>
    private sealed class ReflectionReader<T> : IEntityReader<T>
        where T : class, new()
    {
        private readonly PropertyInfo[] _properties =
            [.. typeof(T).GetProperties().Where(p => p.PropertyType.IsValueType || p.PropertyType == typeof(string))];

        public IReadOnlyList<string> PropertyNames => [.. _properties.Select(p => p.Name)];

        public T Read(DbDataReader reader, ReadOnlySpan<int> ordinals)
        {
            var entity = new T();
            for (var i = 0; i < _properties.Length; i++)
            {
                if (ordinals[i] >= 0 && !reader.IsDBNull(ordinals[i]))
                {
                    _properties[i].SetValue(entity, reader.GetValue(ordinals[i]));
                }
            }

            return entity;
        }
    }

    /// <summary>Sets a navigation by reflection, choosing collection types as EF Core does.</summary>
    private sealed class ReflectionNavigationAccessor(Type entityType, PropertyInfo property) : INavigationAccessor
    {
        public Type EntityType { get; } = entityType;

        public string NavigationName => property.Name;

        public void SetReference(object entity, object? value) => property.SetValue(entity, value);

        public void EnsureCollection(object entity)
        {
            if (property.GetValue(entity) is null)
            {
                var type = property.PropertyType;
                var element = type.GetGenericArguments()[0];
                var hashSet = typeof(HashSet<>).MakeGenericType(element);
                var concrete = !type.IsInterface ? type : type.IsAssignableFrom(hashSet) ? hashSet : typeof(List<>).MakeGenericType(element);
                property.SetValue(entity, Activator.CreateInstance(concrete));
            }
        }

        public void AddToCollection(object entity, object item)
        {
            EnsureCollection(entity);
            var collection = property.GetValue(entity)!;
            var element = property.PropertyType.GetGenericArguments()[0];
            typeof(ICollection<>).MakeGenericType(element).GetMethod(nameof(ICollection<object>.Add))!.Invoke(collection, [item]);
        }
    }
}

/// <summary>Builds result tables whose columns follow a statement's entity segments.</summary>
public static class LibraryRows
{
    /// <summary>A table with one column per property of each entity, in model order.</summary>
    public static DataTable Table(DbContext db, params Type[] segments)
    {
        var table = new DataTable();
        var index = 0;
        foreach (var type in segments)
        {
            foreach (var _ in ModelBinder.GetEntity(db.Model, type).Properties)
            {
                table.Columns.Add("c" + index++, typeof(object)).AllowDBNull = true;
            }
        }

        return table;
    }

    /// <summary>Adds a row; each segment's values are given by property name, and missing ones are null.</summary>
    public static DataTable Row(this DataTable table, DbContext db, params (Type Entity, IDictionary Values)[] segments)
    {
        var values = new List<object>();
        foreach (var (type, named) in segments)
        {
            foreach (var property in ModelBinder.GetEntity(db.Model, type).Properties)
            {
                values.Add(named.Contains(property.Name) ? named[property.Name] ?? DBNull.Value : DBNull.Value);
            }
        }

        table.Rows.Add([.. values]);
        return table;
    }
}
