using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Core.Fields;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Checks at startup that every registered field type has an editor to fill it in with
/// (tasks P6-06 to P6-15, ADR-0014).
/// </summary>
/// <remarks>
/// The backoffice's equivalent of <see cref="CmsRenderingStartupService"/>, and this is the only
/// place the check can run: the editor catalog lives in <c>Client</c>, the field type registry lives
/// in <c>Core</c>, and the browser can see only the first of the two. The server references both.
/// <para>
/// <strong>A missing editor is worse than a missing renderer, and is still not fatal.</strong> Worse,
/// because a field type with no renderer costs a reader a paragraph while a field type with no editor
/// leaves an author with no way at all to fill a property their template marks required — and
/// therefore no way to publish the page. Not fatal, because the catalog always falls back to showing
/// the stored value read-only, and a site that will not start serves nothing at all. What is not
/// acceptable is it being silent, which is what this line is for.
/// </para>
/// </remarks>
/// <param name="editors">The field type to editor mapping, rebuilt here against the registry.</param>
/// <param name="registry">The field types this deployment registered.</param>
/// <param name="logger">Log for the report.</param>
public sealed class CmsEditorStartupService(
    IFieldEditorCatalog editors,
    IFieldTypeRegistry registry,
    ILogger<CmsEditorStartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Rebuilt rather than read off the injected instance, because the registered instance is
        // the one the browser also builds — with nothing to compare itself against. Resolving it
        // above is still worthwhile: it forces the table to be validated while the host is starting,
        // so an entry pointing at something that is not a Razor component is a startup exception
        // rather than a broken zone card in production.
        var checkedCatalog = FieldEditorCatalog.For([.. registry.All.Select(fieldType => fieldType.Key)]);

        logger.LogInformation(
            "CMS editors ready: {EditorCount} field editor(s) for {FieldTypeCount} registered field type(s).",
            editors.FieldTypeKeys.Count,
            registry.All.Count);

        if (checkedCatalog.FieldTypesWithNoEditor.Count > 0)
        {
            logger.LogError(
                "No editor is registered for field type(s) {FieldTypeKeys}. A property using one " +
                "shows an author its stored value read-only and cannot be filled in, so a template " +
                "that marks it required cannot be published. Register an editor for the key in the " +
                "field editor catalog.",
                string.Join(", ", checkedCatalog.FieldTypesWithNoEditor));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
