using KetPsi.EntityFrameworkCore.Binding;
using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Binding;

public sealed class ModelBinderTests
{
    [Fact]
    public void GetEntity_ReadsTableAndSchemaFromModel()
    {
        var entity = Bind<ProductContext, Product>();

        Assert.Equal(typeof(Product), entity.ClrType);
        Assert.Equal("Products", entity.Table);
        Assert.Equal("catalog", entity.Schema);
    }

    [Fact]
    public void GetEntity_DefaultMapping_UsesEfConventions()
    {
        var entity = Bind<PlainProductContext, Product>();

        Assert.Equal("Product", entity.Table);
        Assert.Null(entity.Schema);
        Assert.Equal("Name", entity.GetProperty(nameof(Product.Name)).ColumnName);
    }

    [Fact]
    public void GetEntity_BindsEveryPropertyIncludingShadow()
    {
        var entity = Bind<ProductContext, Product>();

        Assert.Equal(
            [
                new BoundProperty(ProductContext.CreatedAtShadowProperty, "CreatedAt", "datetime2", IsNullable: false, typeof(DateTime), IsShadow: true),
                new BoundProperty(nameof(Product.Description), "Description", "nvarchar(max)", IsNullable: true, typeof(string), IsShadow: false),
                new BoundProperty(nameof(Product.ExternalId), "ExternalId", "uniqueidentifier", IsNullable: false, typeof(Guid), IsShadow: false),
                new BoundProperty(nameof(Product.Id), "Id", "int", IsNullable: false, typeof(int), IsShadow: false),
                new BoundProperty(nameof(Product.IsActive), "IsActive", "bit", IsNullable: false, typeof(bool), IsShadow: false),
                new BoundProperty(nameof(Product.Name), "ProductName", "nvarchar(100)", IsNullable: false, typeof(string), IsShadow: false),
                new BoundProperty(nameof(Product.Price), "Price", "decimal(18,2)", IsNullable: false, typeof(decimal), IsShadow: false),
                new BoundProperty(nameof(Product.Stock), "Stock", "int", IsNullable: true, typeof(int?), IsShadow: false),
            ],
            entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void GetEntity_SingleKey_IsTheKeyProperty()
    {
        var entity = Bind<ProductContext, Product>();

        var key = Assert.Single(entity.PrimaryKey!);
        Assert.Same(entity.GetProperty(nameof(Product.Id)), key);
    }

    // Declared as (StudentId, CourseId), the reverse of alphabetical order, so the order can only come from the key.
    [Fact]
    public void GetEntity_CompositeKey_IsInDeclaredKeyOrder()
    {
        var entity = Bind<CompositeKeyContext, Enrollment>();

        Assert.Equal([nameof(Enrollment.StudentId), nameof(Enrollment.CourseId)], entity.PrimaryKey!.Select(p => p.Name));
        Assert.All(entity.PrimaryKey!, key => Assert.Same(entity.GetProperty(key.Name), key));
    }

    [Fact]
    public void GetEntity_Keyless_HasNoPrimaryKey()
    {
        var entity = Bind<KeylessContext, Report>();

        Assert.Null(entity.PrimaryKey);
        Assert.Equal("Reports", entity.Table);
    }

    [Fact]
    public void GetEntity_ShadowProperties_AreListedAndMarked()
    {
        var entity = Bind<ProductContext, Product>();

        var shadow = Assert.Single(entity.ShadowProperties);
        Assert.Equal(ProductContext.CreatedAtShadowProperty, shadow.Name);
        Assert.True(shadow.IsShadow);
        Assert.Same(entity.GetProperty(ProductContext.CreatedAtShadowProperty), shadow);
        Assert.False(entity.GetProperty(nameof(Product.Name)).IsShadow);
    }

    [Fact]
    public void GetEntity_NoShadowProperties_IsEmpty()
        => Assert.Empty(Bind<PlainProductContext, Product>().ShadowProperties);

    [Fact]
    public void GetEntity_SameModelAndType_ReturnsCachedInstance()
    {
        using var db = new ProductContext();

        Assert.Same(ModelBinder.GetEntity(db.Model, typeof(Product)), ModelBinder.GetEntity(db.Model, typeof(Product)));
    }

    [Fact]
    public void GetEntity_SameTypeInDifferentModels_BindsEachModel()
    {
        var configured = Bind<ProductContext, Product>();
        var plain = Bind<PlainProductContext, Product>();

        Assert.NotSame(configured, plain);
        Assert.Equal("Products", configured.Table);
        Assert.Equal("Product", plain.Table);
    }

    [Fact]
    public void GetEntity_TypeNotInModel_Throws()
    {
        using var db = new ProductContext();

        var exception = Assert.Throws<InvalidOperationException>(() => ModelBinder.GetEntity(db.Model, typeof(Blog)));

        Assert.Contains(typeof(Blog).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEntity_ValueConverter_ThrowsNamingProperty()
    {
        var exception = Assert.Throws<NotSupportedException>(Bind<ConverterContext, Product>);

        Assert.Contains($"{typeof(Product)}.{nameof(Product.Name)}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEntity_BaseOfHierarchy_Throws()
        => Assert.Throws<NotSupportedException>(Bind<InheritanceContext, Animal>);

    [Fact]
    public void GetEntity_DerivedInHierarchy_Throws()
        => Assert.Throws<NotSupportedException>(Bind<InheritanceContext, Dog>);

    [Fact]
    public void GetEntity_OwnerOfOwnedType_Throws()
        => Assert.Throws<NotSupportedException>(Bind<OwnedContext, Order>);

    [Fact]
    public void GetEntity_ComplexProperty_Throws()
        => Assert.Throws<NotSupportedException>(Bind<ComplexContext, Customer>);

    [Fact]
    public void GetEntity_MappedToView_Throws()
        => Assert.Throws<NotSupportedException>(Bind<ViewContext, Product>);

    [Fact]
    public void GetEntity_NullArguments_Throw()
    {
        using var db = new ProductContext();

        Assert.Throws<ArgumentNullException>("model", () => ModelBinder.GetEntity(null!, typeof(Product)));
        Assert.Throws<ArgumentNullException>("clrType", () => ModelBinder.GetEntity(db.Model, null!));
    }

    [Fact]
    public void GetProperty_UnknownName_ThrowsNamingEntityAndProperty()
    {
        var entity = Bind<ProductContext, Product>();

        var exception = Assert.Throws<InvalidOperationException>(() => entity.GetProperty("Missing"));

        Assert.Contains(typeof(Product).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Missing'", exception.Message, StringComparison.Ordinal);
    }

    internal static BoundEntity Bind<TContext, TEntity>()
        where TContext : BindingContext, new()
    {
        using var db = new TContext();
        return ModelBinder.GetEntity(db.Model, typeof(TEntity));
    }
}
