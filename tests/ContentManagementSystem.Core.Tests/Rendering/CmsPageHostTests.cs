using Bunit;

using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// Composition from the root down: host → template → zone → field renderer (task P3-08).
/// </summary>
/// <remarks>
/// The S2 spike proved this composes under static SSR with nothing switched on statically. What
/// these tests hold in place is that it stays that way — a template names a zone key and nothing
/// else, and every hop is a lookup.
/// </remarks>
public class CmsPageHostTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly RecordingLoggerProvider _logs = new();

    public CmsPageHostTests()
    {
        _bunit.Services.AddLogging(logging => logging.AddProvider(_logs));
        _bunit.Services.AddSingleton<IFieldRendererCatalog>(new TestFieldRendererCatalog(
            ("plainText", typeof(RecordingFieldRenderer))));
        _bunit.Services.AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
            CmsComponentScanner.ScanTypes([typeof(TestTemplate), typeof(TestBlock)])));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        _logs.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ATemplateResolvedFromTheStoredKeyRendersItsZonesThroughTheCascadedContext()
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload(
            ("hero", """{"type":"plainText","value":"Hello"}""")));

        var markup = _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context)).Markup;

        // Four levels in one assertion: the host resolved the template by key, the template read the
        // page off the cascaded context, the zone resolved a renderer, and the renderer read the
        // stored value.
        markup.Should().Contain("<article")
            .And.Contain("data-title=\"About us\"")
            .And.Contain("data-renderer=\"recording\"")
            .And.Contain("Hello");
    }

    [Fact]
    public void AnUnknownTemplateKeyLogsAnErrorRatherThanThrowing()
    {
        var context = RenderingHarness.Context(
            RenderingHarness.Payload(),
            templateKey: "template-this-deployment-lost");

        var markup = _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context)).Markup;

        // Spec section 15.3's first row. The fallback layout that replaces the empty render is
        // task P3-11; what must already hold is that the request survives and says why.
        markup.Should().BeEmpty();
        _logs.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("template-this-deployment-lost"));
    }
}
