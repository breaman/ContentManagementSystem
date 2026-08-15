namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// Marks an endpoint as antiforgery-protected, so the protection is visible in the route table.
/// </summary>
/// <remarks>
/// A filter added with <c>AddEndpointFilter</c> lives in the builder's filter pipeline and leaves no
/// trace in <c>Endpoint.Metadata</c>. That makes "is every write protected?" a question nothing can
/// answer by inspection — and it is precisely the question worth being able to answer automatically,
/// because the way this defence fails is a new endpoint that simply forgot it.
/// </remarks>
public sealed class CmsAntiforgeryMetadata
{
    /// <summary>The single marker instance, since it carries no state.</summary>
    public static CmsAntiforgeryMetadata Instance { get; } = new();

    private CmsAntiforgeryMetadata()
    {
    }
}

/// <summary>
/// Registers <see cref="CmsAntiforgeryFilter"/> together with its marker.
/// </summary>
public static class CmsAntiforgeryExtensions
{
    /// <summary>
    /// Requires a valid antiforgery token on the endpoint.
    /// </summary>
    /// <param name="builder">The endpoint being built.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Use this on every state-changing management endpoint rather than adding the filter directly:
    /// the two have to arrive together, and a contract test asserts over the marker.
    /// </remarks>
    public static RouteHandlerBuilder RequireCmsAntiforgery(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddEndpointFilter<CmsAntiforgeryFilter>()
            .WithMetadata(CmsAntiforgeryMetadata.Instance);
    }
}
