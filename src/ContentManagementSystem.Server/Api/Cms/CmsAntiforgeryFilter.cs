using System.Net;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Antiforgery;

namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// Requires a valid antiforgery token on every state-changing management API request
/// (spec section 22).
/// </summary>
/// <remarks>
/// The management API authenticates with the Identity cookie, which a browser attaches to
/// cross-site requests as readily as to same-site ones. Without this, any page a signed-in developer
/// visited could <c>POST</c> to the structure endpoints on their behalf.
/// <para>
/// This is an endpoint filter rather than middleware because the antiforgery middleware already in
/// the pipeline only validates endpoints that bind form data. A JSON body binds through a different
/// path and is not covered by it — which is the quiet half of this class's reason to exist.
/// </para>
/// </remarks>
public sealed class CmsAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // No detail about which half of the token was wrong: the only caller who benefits from
            // that is one guessing.
            return CmsProblems.Problem(
                HttpStatusCode.BadRequest,
                "antiforgery",
                "Missing or invalid antiforgery token",
                ValidationResult.Error(
                    "request.antiforgery",
                    "This request needs a current antiforgery token. Fetch one from " +
                    "/api/cms/v1/antiforgery-token and send it in the request header."));
        }

        return await next(context);
    }
}
