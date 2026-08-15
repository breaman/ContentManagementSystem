using System.Text;

using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Routing;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Routing;

/// <summary>
/// <c>/api/cms/v1/redirects</c> — the redirect table and its bulk import (tasks P3-05 and P3-06).
/// </summary>
/// <remarks>
/// Writes require <c>Content.Publish</c> rather than <c>Content.Edit</c>. A redirect changes what
/// anonymous visitors see the instant it is saved, with no draft, no preview, and no publish step to
/// pass through — it belongs with the permission that already means "may change the public site"
/// (spec section 21.1).
/// <para>
/// There is no <c>PATCH</c> of <c>fromUrl</c>. A redirect <em>is</em> its source URL; changing it is
/// deleting one rule and creating another, and an edit that quietly did both would leave the
/// original URL serving nothing with no record that it ever did.
/// </para>
/// </remarks>
public static class RedirectEndpoints
{
    /// <summary>Path segment this resource hangs off, shared with the import and export routes.</summary>
    public const string Prefix = "/redirects";

    /// <summary>Media type the export is served as.</summary>
    private const string CsvContentType = "text/csv";

    /// <summary>
    /// Maps the redirect endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapRedirectEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var redirects = group.MapGroup(Prefix).WithTags("Redirects");

        redirects.MapGet("/", ListAsync)
            .WithName("ListRedirects")
            .WithSummary("Lists redirects, newest first, with their resolved destinations.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        redirects.MapPost("/", CreateAsync)
            .WithName("CreateRedirect")
            .WithSummary("Creates a hand-entered redirect.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        redirects.MapPatch("/{id:int}", UpdateAsync)
            .WithName("UpdateRedirect")
            .WithSummary("Changes a redirect's destination, status, or enabled state.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        redirects.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteRedirect")
            .WithSummary("Removes a redirect outright.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        redirects.MapPost("/import", ImportAsync)
            .WithName("ImportRedirects")
            .WithSummary("Bulk-imports redirects from CSV, warning about each row it skipped.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery()
            .Accepts<string>(CsvContentType);

        redirects.MapGet("/export", ExportAsync)
            .WithName("ExportRedirects")
            .WithSummary("Exports every redirect as CSV, in the shape the import reads.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    private static async Task<IResult> ListAsync(
        IRedirectService redirects,
        CancellationToken cancellationToken,
        string? search = null,
        string? cursor = null,
        int? limit = null) =>
        (await redirects.ListAsync(search, cursor, Cursor.Clamp(limit), cancellationToken))
        .ToHttpResult(Results.Ok);

    private static async Task<IResult> CreateAsync(
        CreateRedirectRequest request,
        IRedirectService redirects,
        CancellationToken cancellationToken) =>
        (await redirects.CreateAsync(request, cancellationToken))
        .ToHttpResult(detail => Results.Created($"{CmsApiEndpoints.BasePath}{Prefix}/{detail.Id}", detail));

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateRedirectRequest request,
        IRedirectService redirects,
        CancellationToken cancellationToken) =>
        (await redirects.UpdateAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteAsync(
        int id,
        IRedirectService redirects,
        CancellationToken cancellationToken) =>
        (await redirects.DeleteAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());

    /// <remarks>
    /// The body is read as raw text rather than bound as JSON, because what an operator has is a CSV
    /// file exported from a legacy CMS or a spreadsheet. Requiring them to wrap thousands of lines
    /// in a JSON string first is a step whose only purpose would be to suit the binder.
    /// </remarks>
    private static async Task<IResult> ImportAsync(
        HttpRequest request,
        IRedirectService redirects,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(cancellationToken);

        var result = await redirects.ImportAsync(csv, cancellationToken);

        // Warnings survive a success here for the same reason they do on a zone save: every skipped
        // row is reported with its line number, and dropping them would leave the operator with a
        // count and no way to find out which lines it referred to.
        return result.ToHttpResult(summary => Results.Ok(new
        {
            summary.Created,
            summary.Updated,
            summary.Skipped,
            Warnings = ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning),
        }));
    }

    private static async Task<IResult> ExportAsync(
        IRedirectService redirects,
        CancellationToken cancellationToken) =>
        (await redirects.ExportAsync(cancellationToken))
        .ToHttpResult(csv => Results.File(
            Encoding.UTF8.GetBytes(csv),
            CsvContentType,
            "redirects.csv"));
}
