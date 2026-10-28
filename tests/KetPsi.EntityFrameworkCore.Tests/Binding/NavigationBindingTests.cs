using KetPsi.EntityFrameworkCore.Tests.TestModel;

namespace KetPsi.EntityFrameworkCore.Tests.Binding;

public sealed class NavigationBindingTests
{
    [Fact]
    public void Collection_IsOnPrincipalAndJoinsKeyToForeignKey()
    {
        var writer = ModelBinderTests.Bind<LibraryContext, Writer>();

        var books = writer.GetNavigation(nameof(Writer.Books));

        Assert.True(books.IsCollection);
        Assert.False(books.IsOnDependent);
        Assert.False(books.IsRequired);
        Assert.Equal(typeof(ICollection<Book>), books.ClrType);
        Assert.Equal(typeof(Book), books.TargetClrType);
        Assert.Equal(nameof(Book.Writer), books.InverseName);
        Assert.Equal([nameof(Writer.Id)], books.SourceKey.Select(p => p.Name));
        Assert.Equal([nameof(Book.WriterId)], books.TargetKey);
    }

    [Fact]
    public void RequiredReference_IsOnDependentAndJoinsForeignKeyToKey()
    {
        var book = ModelBinderTests.Bind<LibraryContext, Book>();

        var writer = book.GetNavigation(nameof(Book.Writer));

        Assert.False(writer.IsCollection);
        Assert.True(writer.IsOnDependent);
        Assert.True(writer.IsRequired);
        Assert.Equal(typeof(Writer), writer.ClrType);
        Assert.Equal(typeof(Writer), writer.TargetClrType);
        Assert.Equal(nameof(Writer.Books), writer.InverseName);
        Assert.Equal([nameof(Book.WriterId)], writer.SourceKey.Select(p => p.Name));
        Assert.Equal([nameof(Writer.Id)], writer.TargetKey);
    }

    [Fact]
    public void OptionalReference_WithoutInverse_IsNotRequired()
    {
        var book = ModelBinderTests.Bind<LibraryContext, Book>();

        var editor = book.GetNavigation(nameof(Book.Editor));

        Assert.False(editor.IsRequired);
        Assert.Null(editor.InverseName);
        Assert.Equal([nameof(Book.EditorId)], editor.SourceKey.Select(p => p.Name));
    }

    [Fact]
    public void Collection_KeepsDeclaredCollectionType()
        => Assert.Equal(typeof(List<Review>), ModelBinderTests.Bind<LibraryContext, Book>().GetNavigation(nameof(Book.Reviews)).ClrType);

    [Fact]
    public void ShadowForeignKey_IsBoundAsShadowProperty()
    {
        var review = ModelBinderTests.Bind<LibraryContext, Review>();

        var book = review.GetNavigation(nameof(Review.Book));

        var foreignKey = Assert.Single(book.SourceKey);
        Assert.True(foreignKey.IsShadow);
        Assert.True(foreignKey.IsNullable);
        Assert.False(book.IsRequired);
        Assert.Equal([nameof(Book.Id)], book.TargetKey);
    }

    [Fact]
    public void SourceKey_UsesTheEntitysBoundProperties()
    {
        var book = ModelBinderTests.Bind<LibraryContext, Book>();

        var writer = book.GetNavigation(nameof(Book.Writer));

        Assert.Same(book.GetProperty(nameof(Book.WriterId)), Assert.Single(writer.SourceKey));
    }

    [Fact]
    public void OneToOne_PrincipalReferenceIsOptional_DependentReferenceIsRequired()
    {
        var profile = ModelBinderTests.Bind<LibraryContext, Writer>().GetNavigation(nameof(Writer.Profile));
        var writer = ModelBinderTests.Bind<LibraryContext, WriterProfile>().GetNavigation(nameof(WriterProfile.Writer));

        Assert.False(profile.IsCollection);
        Assert.False(profile.IsOnDependent);
        Assert.False(profile.IsRequired);
        Assert.Equal([nameof(Writer.Id)], profile.SourceKey.Select(p => p.Name));
        Assert.Equal([nameof(WriterProfile.WriterId)], profile.TargetKey);
        Assert.True(writer.IsOnDependent);
        Assert.True(writer.IsRequired);
    }

    [Fact]
    public void OneToOne_RequiredDependent_MakesPrincipalReferenceRequired()
        => Assert.True(ModelBinderTests.Bind<RequiredProfileLibraryContext, Writer>().GetNavigation(nameof(Writer.Profile)).IsRequired);

    [Fact]
    public void CompositeForeignKey_PairsColumnsInKeyOrder()
    {
        var section = ModelBinderTests.Bind<LibraryContext, Locker>().GetNavigation(nameof(Locker.Section));
        var lockers = ModelBinderTests.Bind<LibraryContext, Section>().GetNavigation(nameof(Section.Lockers));

        Assert.Equal([nameof(Locker.SectionFloor), nameof(Locker.SectionCode)], section.SourceKey.Select(p => p.Name));
        Assert.Equal([nameof(Section.Floor), nameof(Section.Code)], section.TargetKey);
        Assert.Equal([nameof(Section.Floor), nameof(Section.Code)], lockers.SourceKey.Select(p => p.Name));
        Assert.Equal([nameof(Locker.SectionFloor), nameof(Locker.SectionCode)], lockers.TargetKey);
    }

    [Fact]
    public void ManyToMany_IsNotBound_AndRequestingItThrows()
    {
        var book = ModelBinderTests.Bind<LibraryContext, Book>();

        var exception = Assert.Throws<NotSupportedException>(() => book.GetNavigation(nameof(Book.Tags)));

        Assert.DoesNotContain(book.Navigations, n => n.Name == nameof(Book.Tags));
        Assert.Contains($"'{typeof(Book)}.{nameof(Book.Tags)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ThenInclude", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyToManyEntity_StillBinds()
        => Assert.Empty(ModelBinderTests.Bind<LibraryContext, Tag>().Navigations);

    [Fact]
    public void Navigations_AreInModelOrder()
        => Assert.Equal(
            [nameof(Book.Editor), nameof(Book.Reviews), nameof(Book.Writer)],
            ModelBinderTests.Bind<LibraryContext, Book>().Navigations.Select(n => n.Name));

    [Fact]
    public void GetNavigation_UnknownName_ThrowsNamingEntityAndNavigation()
    {
        var book = ModelBinderTests.Bind<LibraryContext, Book>();

        var exception = Assert.Throws<InvalidOperationException>(() => book.GetNavigation("Missing"));

        Assert.Contains(typeof(Book).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityWithoutNavigations_HasNone()
        => Assert.Empty(ModelBinderTests.Bind<ProductContext, Product>().Navigations);
}
