using ContentManagementSystem.Rendering;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Checks at startup that every registered field type has a renderer to draw it with (task P3-09).
/// </summary>
/// <remarks>
/// The whole value of this check is <em>when</em> it runs. Resolving the two singletons here forces
/// the component scan and the renderer mapping to happen while the host is starting, so a
/// <c>[CmsTemplate]</c> on a class that is not a component, or two components claiming one key, is
/// an exception at deployment time rather than on whichever public request first reaches the
/// affected page.
/// <para>
/// A field type with no renderer is reported rather than thrown. Unlike a duplicate key it has a
/// defensible outcome — the pages using it render that zone as nothing and log, exactly as they
/// would for a field type key this deployment never knew (spec section 15.3) — and every other page
/// on the site is unaffected. A site that will not start serves nothing at all, which is the worse
/// of the two failures. What is not acceptable is it being <em>silent</em>: without this line the
/// omission is invisible until a reader notices a missing paragraph.
/// </para>
/// </remarks>
/// <param name="renderers">The field type to renderer mapping, resolved to force it to be built.</param>
/// <param name="components">The template and block type catalog, resolved for the same reason.</param>
/// <param name="logger">Log for the report.</param>
public sealed class CmsRenderingStartupService(
    IFieldRendererCatalog renderers,
    ICmsComponentCatalog components,
    ILogger<CmsRenderingStartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CMS rendering ready: {TemplateCount} template(s), {BlockTypeCount} block type(s), " +
            "{RendererCount} field renderer(s).",
            components.TemplateKeys.Count,
            components.BlockTypeKeys.Count,
            renderers.FieldTypeKeys.Count);

        if (renderers.FieldTypesWithNoRenderer.Count > 0)
        {
            logger.LogError(
                "No renderer is registered for field type(s) {FieldTypeKeys}. Content authored " +
                "against them renders nothing on the public site. Register a renderer for the key, " +
                "or have the field type name one through IFieldType.RendererComponent.",
                string.Join(", ", renderers.FieldTypesWithNoRenderer));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
