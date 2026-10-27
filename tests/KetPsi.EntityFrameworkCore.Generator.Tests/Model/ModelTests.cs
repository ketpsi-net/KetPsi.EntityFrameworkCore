using System.Collections.Immutable;
using System.Globalization;
using KetPsi.EntityFrameworkCore.Generator.Diagnostics;
using KetPsi.EntityFrameworkCore.Generator.Discovery;
using KetPsi.EntityFrameworkCore.Generator.Model;
using KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Model;

public sealed class ModelTests
{
    [Fact]
    public void EquatableArray_SameElements_AreEqualWithSameHash()
    {
        var first = new[] { "a", "b" }.ToEquatableArray();
        var second = new[] { "a", "b" }.ToEquatableArray();

        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first.Equals((object)second));
    }

    [Fact]
    public void EquatableArray_DifferentElements_AreNotEqual()
    {
        Assert.True(new[] { "a" }.ToEquatableArray() != new[] { "b" }.ToEquatableArray());
        Assert.NotEqual(new[] { "a" }.ToEquatableArray(), new[] { "a", "b" }.ToEquatableArray());
    }

    [Fact]
    public void EquatableArray_Default_BehavesAsEmpty()
    {
        var defaultArray = default(EquatableArray<string>);

        Assert.Empty(defaultArray);
        Assert.True(defaultArray.AsImmutableArray().IsEmpty);
        Assert.Equal(EquatableArray<string>.Empty, defaultArray);
        Assert.Equal(EquatableArray<string>.Empty.GetHashCode(), defaultArray.GetHashCode());
    }

    [Fact]
    public void EquatableArray_IndexerAndEnumeration_ReturnElements()
    {
        var array = new EquatableArray<string>(ImmutableArray.Create("x", "y"));

        Assert.Equal("y", array[1]);
        Assert.Equal(["x", "y"], array);
        Assert.Equal(2, array.AsImmutableArray().Length);
    }

    [Fact]
    public void LocationInfo_RoundTripsLocation()
    {
        var location = Location.Create("file.cs", new TextSpan(5, 3), new LinePositionSpan(new LinePosition(1, 2), new LinePosition(1, 5)));

        var info = LocationInfo.From(location);
        var restored = info.ToLocation();

        Assert.Equal("file.cs", info.FilePath);
        Assert.Equal(location.SourceSpan, restored.SourceSpan);
        Assert.Equal(location.GetLineSpan(), restored.GetLineSpan());
        Assert.Equal(info, LocationInfo.From(location));
    }

    [Fact]
    public void DiagnosticInfo_CreatesDiagnosticWithArguments()
    {
        var location = new LocationInfo("file.cs", new TextSpan(0, 6), new LinePositionSpan(LinePosition.Zero, new LinePosition(0, 6)));

        var diagnostic = DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedOperator, location, "Select").ToDiagnostic();

        Assert.Equal("KP1001", diagnostic.Id);
        Assert.Equal("The query operator 'Select' is not supported by KetPsi.EntityFrameworkCore", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(location.Span, diagnostic.Location.SourceSpan);
    }

    [Fact]
    public void DiagnosticInfo_WithoutLocation_UsesNone()
        => Assert.Equal(
            Location.None,
            DiagnosticInfo.Create(DiagnosticDescriptors.InterceptorsNotEnabled, location: null, "Ns").ToDiagnostic().Location);

    [Fact]
    public void DiagnosticInfo_SameContent_IsEqual()
        => Assert.Equal(
            DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedOperator, location: null, "Select"),
            DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedOperator, location: null, "Select"));

    [Fact]
    public void DiagnosticDescriptors_AreAllErrorsInOneCategory()
    {
        DiagnosticDescriptor[] descriptors =
        [
            DiagnosticDescriptors.InternalError,
            DiagnosticDescriptors.InterceptorsNotEnabled,
            DiagnosticDescriptors.UnsupportedOperator,
            DiagnosticDescriptors.IncludeNotSupported,
            DiagnosticDescriptors.TrackingNotSupported,
            DiagnosticDescriptors.RawSqlNotSupported,
            DiagnosticDescriptors.LambdaRequired,
            DiagnosticDescriptors.UnsupportedOverload,
        ];

        Assert.All(descriptors, d =>
        {
            Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity);
            Assert.Equal("KetPsi.EntityFrameworkCore", d.Category);
            Assert.StartsWith("KP", d.Id, StringComparison.Ordinal);
        });
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(QueryInterceptorGenerator.InterceptorsNamespacesProperty, "KetPsi.EntityFrameworkCore.Generated", true)]
    [InlineData(QueryInterceptorGenerator.InterceptorsNamespacesProperty, "A;KetPsi.EntityFrameworkCore.Generated", true)]
    [InlineData(QueryInterceptorGenerator.InterceptorsNamespacesProperty, "A, KetPsi.EntityFrameworkCore.Generated ,B", true)]
    [InlineData(QueryInterceptorGenerator.InterceptorsPreviewNamespacesProperty, ";KetPsi.EntityFrameworkCore.Generated", true)]
    [InlineData(QueryInterceptorGenerator.KetPsiInterceptorsNamespacesProperty, "list:;KetPsi.EntityFrameworkCore.Generated", true)]
    [InlineData(QueryInterceptorGenerator.KetPsiInterceptorsNamespacesProperty, "list:A;B", false)]
    [InlineData(QueryInterceptorGenerator.KetPsiInterceptorsNamespacesProperty, "list:", false)]
    [InlineData(QueryInterceptorGenerator.InterceptorsNamespacesProperty, "KetPsi.EntityFrameworkCore", false)]
    [InlineData(QueryInterceptorGenerator.InterceptorsNamespacesProperty, "", false)]
    public void InterceptorsEnabled_ReadsNamespaceLists(string property, string value, bool expected)
        => Assert.Equal(expected, QueryInterceptorGenerator.InterceptorsEnabled(
            new TestAnalyzerConfigOptions(new Dictionary<string, string> { [property] = value })));

    [Fact]
    public void InterceptorsEnabled_NoProperty_IsFalse()
        => Assert.False(QueryInterceptorGenerator.InterceptorsEnabled(new TestAnalyzerConfigOptions(new Dictionary<string, string>())));

    [Fact]
    public void ForEntities_KeepsEntitySitesAndInternalErrors()
    {
        var location = new LocationInfo("f.cs", default, default);
        var entitySite = new CallSite("global::App.Blog", location, OperatorKind.Where, IsAsync: false, HasPredicate: false, Diagnostic: null);
        var otherSite = new CallSite("global::App.Other", location, OperatorKind.Where, IsAsync: false, HasPredicate: false, Diagnostic: null);
        var error = new CallSite(CallSiteAnalyzer.UnknownElementType, location, Operator: null, IsAsync: false, HasPredicate: false, Diagnostic: null);

        var kept = QueryInterceptorGenerator.ForEntities(
            ImmutableArray.Create<CallSite?>(entitySite, otherSite, null, error),
            new[] { "global::App.Blog" }.ToEquatableArray());

        Assert.Equal([entitySite, error], kept);
    }
}
