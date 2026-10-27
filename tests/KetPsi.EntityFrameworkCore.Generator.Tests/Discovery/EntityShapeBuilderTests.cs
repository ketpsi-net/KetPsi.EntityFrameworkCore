using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Emit;
using KetPsi.EntityFrameworkCore.Generator.Model;
using KetPsi.EntityFrameworkCore.Generator.Tests.Emit;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Discovery;

public sealed class EntityShapeBuilderTests
{
    private static readonly LocationInfo Fallback = new("f.cs", default, default);

    [Theory]
    [InlineData("global::App.Blog", "App_Blog_Reader")]
    [InlineData("global::App.Outer.Inner", "App_Outer_Inner_Reader")]
    [InlineData("global::App.Box<global::App.Item>", "App_Box_App_Item__Reader")]
    public void ReaderName_IsValidIdentifier(string typeName, string expected)
        => Assert.Equal(expected, EntityShapeBuilder.ReaderName(typeName));

    [Fact]
    public void Build_ClassifiesEveryPropertyKind()
    {
        var shape = Build("App.Article");

        Assert.Equal(Construction.New, shape.Construction);
        Assert.True(shape.CanRead);
        Assert.Equal("global::App.Article", shape.TypeName);
        Assert.Equal("App_Article_Reader", shape.ReaderName);

        var properties = shape.Properties.ToDictionary(p => p.Name);
        Assert.DoesNotContain("Display", properties.Keys);
        Assert.DoesNotContain("Computed", properties.Keys);
        Assert.DoesNotContain("Author", properties.Keys);
        Assert.DoesNotContain("Comments", properties.Keys);

        Assert.Equal(ReadMethod.GetInt32, properties["Id"].Read);
        Assert.Equal("global::App.EntityBase", properties["Id"].DeclaringTypeName);
        Assert.Equal(Setter.Accessor, properties["CreatedAt"].Setter);
        Assert.Equal("global::App.EntityBase", properties["CreatedAt"].DeclaringTypeName);
        Assert.Equal(Setter.Accessor, properties["Secret"].Setter);
        Assert.Equal(Setter.Init, properties["Slug"].Setter);
        Assert.Equal(Setter.Direct, properties["Title"].Setter);

        Assert.True(properties["Title"].CanBeNull);
        Assert.True(properties["Views"].CanBeNull);
        Assert.False(properties["Total"].CanBeNull);

        Assert.Equal((ReadMethod.GetInt32, "int"), (properties["Status"].Read, properties["Status"].ValueTypeName));
        Assert.Equal((ReadMethod.GetByte, "byte"), (properties["Size"].Read, properties["Size"].ValueTypeName));
        Assert.True(properties["Size"].CanBeNull);
        Assert.Equal((ReadMethod.GetFieldValue, "byte[]"), (properties["Image"].Read, properties["Image"].ValueTypeName));
        Assert.Equal((ReadMethod.GetFieldValue, "global::System.DateOnly"), (properties["PublishedOn"].Read, properties["PublishedOn"].ValueTypeName));
        Assert.Equal(ReadMethod.GetGuid, properties["ExternalId"].Read);
        Assert.Equal(ReadMethod.GetFloat, properties["Ratio"].Read);
    }

    [Fact]
    public void Build_PrivateConstructor_UsesAccessor()
        => Assert.Equal(Construction.Accessor, Build("App.Hidden").Construction);

    [Fact]
    public void Build_SameEntityTwice_IsEqual()
        => Assert.Equal(Build("App.Article"), Build("App.Article"));

    [Fact]
    public void ReadableEntities_DedupesAndSorts()
    {
        var article = Build("App.Article");
        var hidden = Build("App.Hidden");
        CallSite Site(EntityShape? entity, OperatorKind? kind) => new(entity?.TypeName ?? "x", Fallback, kind, false, false, null, entity);

        var entities = QueryInterceptorGenerator.ReadableEntities(new[]
        {
            Site(hidden, OperatorKind.Where),
            Site(article, OperatorKind.ToList),
            Site(article, OperatorKind.Count),
            Site(Build("App.Author"), kind: null),
            Site(null, OperatorKind.Take),
        }.ToEquatableArray());

        Assert.Equal(["global::App.Article", "global::App.Hidden"], entities.Select(e => e.TypeName));
    }

    [Fact]
    public void GetReferences_DetectsKetPsiPackages()
    {
        var withAll = GeneratorHost.Compile("References", "public static class C { }");
        var withoutSqlServer = GeneratorHost.Compile("NoDialect", "public static class C { }", GeneratorHost.SqlServerDialectAssembly);

        Assert.Equal(new GenerationReferences(HasRuntime: true, HasSqlServer: true), QueryInterceptorGenerator.GetReferences(withAll));
        Assert.Equal(new GenerationReferences(HasRuntime: true, HasSqlServer: false), QueryInterceptorGenerator.GetReferences(withoutSqlServer));
    }

    [Fact]
    public void Emit_IsDeterministic()
    {
        var shapes = new[] { Build("App.Article") };

        Assert.Equal(ReaderEmitter.Emit(shapes, registerSqlServer: true), ReaderEmitter.Emit(shapes, registerSqlServer: true));
    }

    private static EntityShape Build(string metadataName)
    {
        var compilation = GeneratorHost.Compile("Shapes", ReaderSources.Source);
        var type = compilation.GetTypeByMetadataName(metadataName)!;
        return EntityShapeBuilder.Build(type, compilation, Fallback);
    }
}
