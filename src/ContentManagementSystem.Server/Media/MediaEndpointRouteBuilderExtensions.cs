namespace ContentManagementSystem.Server.Media;

/// <summary>
/// Maps the media delivery routes (tasks P5-14 to P5-17).
/// </summary>
public static class MediaEndpointRouteBuilderExtensions
{
    /// <summary>Route name of the rendition endpoint, so a test can assert which endpoint matched.</summary>
    public const string RenditionRouteName = "cms-media-rendition";

    /// <summary>Route name of the stored-original endpoint.</summary>
    public const string OriginalRouteName = "cms-media-original";

    /// <summary>
    /// Maps the rendition and original routes under <c>/media</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <c>media</c> is already one of the reserved first segments no page may be published at
    /// (<c>Slugs.Reserved</c>), so these routes cannot be shadowed by content and content cannot
    /// appear to be served from them (ADR 0020).
    /// <para>
    /// Both routes allow anonymous access. Authorization for media is the signature plus the item's
    /// own state: an unsigned URL is refused, and a soft-deleted item disappears behind the query
    /// filter. Requiring a session instead would break every image on the public site.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsMedia(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The name segment carries the extension, which is what names the output format. It is the
        // last segment and is matched loosely on purpose: it is cosmetic, is not part of the
        // signature, and exists so a saved image has a sensible file name.
        endpoints.MapGet(
                "/media/{id:int}/{size}/{mode}/{name}",
                MediaDeliveryEndpoint.HandleRenditionAsync)
            .WithName(RenditionRouteName)
            .AllowAnonymous();

        endpoints.MapGet(
                "/media/{id:int}/file/{name}",
                MediaDeliveryEndpoint.HandleOriginalAsync)
            .WithName(OriginalRouteName)
            .AllowAnonymous();

        return endpoints;
    }
}
