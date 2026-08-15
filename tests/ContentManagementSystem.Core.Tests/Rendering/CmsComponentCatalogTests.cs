using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// Turning stored keys back into components (task P3-08, spec section 15.2).
/// </summary>
/// <remarks>
/// The catalog reads the same scan <c>TemplateReconciler</c> reconciles from, which is the point of
/// the shared scanner: the key a page stores, the row the reconciler writes, and the component that
/// renders it must never be three different answers.
/// <para>
/// These scan explicit type lists rather than this assembly, because two of the cases are
/// declarations that must be <em>refused</em> — and a fixture assembly holding a refused declaration
/// would fail every other test that scanned it. The assembly path itself is exercised by the
/// reconciler's integration tests and by the server's own startup.
/// </para>
/// </remarks>
public class CmsComponentCatalogTests
{
    [Fact]
    public void ADeclaredTemplateAndBlockTypeAreFoundByTheirKeys()
    {
        var catalog = Catalog(typeof(TestTemplate), typeof(TestBlock));

        catalog.TryGetTemplate(RenderingHarness.TemplateKey, out var template).Should().BeTrue();
        template.Should().Be(typeof(TestTemplate));

        catalog.TryGetBlockType("test-block", out var block).Should().BeTrue();
        block.Should().Be(typeof(TestBlock));

        catalog.TemplateKeys.Should().ContainSingle();
        catalog.BlockTypeKeys.Should().ContainSingle();
    }

    [Theory]
    [InlineData("no-such-template")]
    [InlineData("")]
    public void AKeyNoComponentDeclaresReportsFailureRatherThanThrowing(string key)
    {
        // Reached from a stored payload on every request that renders an orphaned template, so the
        // answer has to be a return value rather than an exception. An empty key is the same
        // question asked by a payload whose templateKey member is missing altogether.
        var catalog = Catalog(typeof(TestTemplate));

        catalog.TryGetTemplate(key, out var component).Should().BeFalse();
        component.Should().BeNull();
    }

    [Fact]
    public void TwoComponentsDeclaringOneKeyFailAtStartup()
    {
        // No defensible winner: the key is written into stored payloads, and picking one silently
        // would render half a site with the wrong markup.
        var build = () => Catalog(typeof(FirstDuplicate), typeof(SecondDuplicate));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate-key*");
    }

    [Fact]
    public void ATemplateAttributeOnSomethingThatIsNotAComponentFailsAtStartup()
    {
        // The alternative is a deployment that fails one page at a time, on whichever request first
        // reaches content built on it — a production incident rather than a startup error.
        var build = () => Catalog(typeof(NotAComponent));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a Razor component*");
    }

    private static CmsComponentCatalog Catalog(params Type[] types) =>
        new(CmsComponentScanner.ScanTypes(types));
}

/// <summary>First of two fixtures declaring one key, for the duplicate-key rule.</summary>
[CmsTemplate("duplicate-key", "First")]
internal sealed class FirstDuplicate : CmsTemplateBase;

/// <summary>Second of two fixtures declaring one key.</summary>
[CmsTemplate("duplicate-key", "Second")]
internal sealed class SecondDuplicate : CmsTemplateBase;

/// <summary>A template attribute on a class that cannot render, because it is not a component.</summary>
[CmsTemplate("not-a-component", "Not A Component")]
internal sealed class NotAComponent;
