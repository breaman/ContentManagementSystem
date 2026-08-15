using System.Diagnostics.CodeAnalysis;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// Components and doubles the rendering infrastructure tests render through (task P3-08).
/// </summary>
/// <remarks>
/// The components are written against <see cref="RenderTreeBuilder"/> rather than as <c>.razor</c>
/// files because the unit test project is a plain library, not a Razor one. That is a limitation
/// worth accepting here: what these tests assert is the dispatch — which component the pipeline
/// picks and what it hands it — and none of that depends on how the markup was authored.
/// </remarks>
internal static class RenderingHarness
{
    /// <summary>The template key <see cref="TestTemplate"/> declares.</summary>
    public const string TemplateKey = "test-template";

    /// <summary>Builds a payload carrying the given zones, written as stored JSON.</summary>
    /// <param name="zones">Zone key and stored value JSON pairs.</param>
    public static ContentPayload Payload(params (string Key, string Json)[] zones) =>
        PayloadFor(TemplateKey, zones);

    /// <summary>Builds a payload authored against a named template.</summary>
    /// <param name="templateKey">The template the content declares.</param>
    /// <param name="zones">Zone key and stored value JSON pairs.</param>
    public static ContentPayload PayloadFor(string templateKey, params (string Key, string Json)[] zones)
    {
        var builder = new ContentPayloadBuilder(templateKey, 1);

        foreach (var (key, json) in zones)
        {
            builder.SetZone(key, json);
        }

        return builder.Build();
    }

    /// <summary>Builds a render context over a payload.</summary>
    /// <param name="payload">The content to render.</param>
    /// <param name="schema">The captured schema, when the test supplies one.</param>
    /// <param name="mode">Which audience the render is for.</param>
    /// <param name="templateKey">The template key the page stores.</param>
    public static RenderContext Context(
        ContentPayload payload,
        ContentSchema? schema = null,
        CmsRenderMode mode = CmsRenderMode.Live,
        string templateKey = TemplateKey) =>
        new(Page(templateKey), payload, mode, schema);

    /// <summary>Builds the page facts a render is given.</summary>
    /// <param name="templateKey">The template key the page stores.</param>
    /// <param name="id">The page id.</param>
    /// <param name="templateId">The template id, which the <c>tpl:</c> cache tag is built from.</param>
    public static RenderPage Page(string templateKey = TemplateKey, int id = 44, int templateId = 7) =>
        new(
            Id: id,
            PublicId: Guid.Parse("0f6c1f0a-0000-4000-8000-000000000001"),
            VersionId: 1004,
            VersionNumber: 3,
            Title: "About us",
            Url: "/about",
            TemplateId: templateId,
            TemplateKey: templateKey,
            TemplateRevision: 1,
            IsPublished: true);
}

/// <summary>A field renderer catalog whose contents a test states outright.</summary>
internal sealed class TestFieldRendererCatalog(params (string FieldTypeKey, Type Renderer)[] renderers)
    : IFieldRendererCatalog
{
    private readonly Dictionary<string, Type> _renderers =
        renderers.ToDictionary(entry => entry.FieldTypeKey, entry => entry.Renderer, StringComparer.Ordinal);

    public IReadOnlyCollection<string> FieldTypeKeys => _renderers.Keys;

    public IReadOnlyCollection<string> FieldTypesWithNoRenderer => [];

    public bool TryGetRenderer(string fieldTypeKey, [NotNullWhen(true)] out Type? componentType)
    {
        componentType = null;

        return !string.IsNullOrEmpty(fieldTypeKey) && _renderers.TryGetValue(fieldTypeKey, out componentType);
    }
}

/// <summary>
/// A field renderer that writes what it was handed into the markup, so a test can assert on the
/// dispatch rather than on a component's private state.
/// </summary>
internal sealed class RecordingFieldRenderer : CmsFieldRendererBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "data-renderer", "recording");
        builder.AddAttribute(2, "data-property", PropertyKey);
        builder.AddAttribute(3, "data-max-length", Configuration.GetInt32("maxLength")?.ToString() ?? "-");
        builder.AddContent(4, StringMember("value") ?? string.Empty);
        builder.CloseElement();
    }
}

/// <summary>A second renderer, so a test can tell which of two the pipeline picked.</summary>
internal sealed class AlternateFieldRenderer : CmsFieldRendererBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "em");
        builder.AddAttribute(1, "data-renderer", "alternate");
        builder.AddContent(2, StringMember("value") ?? string.Empty);
        builder.CloseElement();
    }
}

/// <summary>A template declaring one zone, which is what the composition tests render.</summary>
[CmsTemplate(RenderingHarness.TemplateKey, "Test Template")]
internal sealed class TestTemplate : CmsTemplateBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "article");
        builder.AddAttribute(1, "data-title", Page.Title);
        builder.OpenComponent<CmsZone>(2);
        builder.AddComponentParameter(3, nameof(CmsZone.Name), "hero");
        builder.CloseComponent();
        builder.CloseElement();
    }
}

/// <summary>A block component, for the catalog tests.</summary>
[CmsBlockType("test-block", "Test Block")]
internal sealed class TestBlock : CmsBlockBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-block", BlockId.ToString());
        builder.AddContent(2, Text("headline"));
        builder.CloseElement();
    }
}

/// <summary>Captures log entries so a test can assert that a fallback was reported, not just taken.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    private readonly Lock _gate = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    public void Dispose()
    {
    }

    private void Record(LogLevel level, string message)
    {
        lock (_gate)
        {
            _entries.Add((level, message));
        }
    }

    private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Record(logLevel, formatter(state, exception));
    }
}
