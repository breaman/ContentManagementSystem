using System.Net;

using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Preview;

/// <summary>
/// <c>/api/cms/v1/markup-preview</c> — renders authored source through the delivery pipeline so the
/// editor's preview matches the published page (task P6-09, spec section 14.4).
/// </summary>
/// <remarks>
/// <strong>There is one pipeline and this endpoint is how the browser reaches it.</strong>
/// <c>IMarkdownRenderer</c> and <c>IContentSanitizer</c> are singletons in <c>Core</c>, the delivery
/// renderer calls exactly these two, and the backoffice runs in WebAssembly where <c>Core</c> is not
/// loaded. Shipping a second Markdig to the browser would satisfy the screen and break the promise:
/// acceptance criterion P6 #2 requires preview and published output to match, and two converters
/// match only until one of them is upgraded.
/// <para>
/// The response carries what sanitization <em>removed</em> as well as what it kept, which is what
/// makes a preview also a warning — and what the HTML editor's live "these tags will be stripped on
/// save" banner is built on (P6-13, acceptance criterion P6 #3).
/// </para>
/// <para>
/// <c>POST</c> for a read, deliberately. The source is a whole zone's worth of prose; a GET would
/// have to carry it in a query string, where it would be truncated by proxies and written verbatim
/// into every access log the request passes through.
/// </para>
/// </remarks>
public static class MarkupPreviewEndpoints
{
    /// <summary>Path segment this resource hangs off.</summary>
    public const string Prefix = "/markup-preview";

    /// <summary>
    /// Maps the markup preview endpoint into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapMarkupPreviewEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Content.Read rather than Content.Edit: previewing is reading, a Viewer opening a draft
        // sees the same rendered zones an editor does, and rendering somebody's own typing back to
        // them grants no access they did not already have.
        group.MapPost(Prefix, RenderAsync)
            .WithName("RenderMarkupPreview")
            .WithSummary("Renders markdown or HTML through the same pipeline the public site uses.")
            .WithTags("Preview")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        // The permitted-tags banner (task P6-13). A read of what this deployment will do to markup
        // the caller is already authorized to author, so it needs nothing narrower than reading.
        group.MapGet($"{Prefix}/profiles", ListProfiles)
            .WithName("ListSanitizationProfiles")
            .WithSummary("Lists the elements each sanitization profile keeps.")
            .WithTags("Preview")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    /// <remarks>
    /// All three profiles in one response rather than one per request. There are three of them, they
    /// are constant for the lifetime of the process, and the alternative is the editor making a
    /// request every time a zone with a different profile comes into view.
    /// </remarks>
    private static IResult ListProfiles() =>
        Results.Ok(Enum.GetValues<SanitizationProfile>()
            .Select(profile => new SanitizationProfileDescriptor(
                profile.ToString(),
                [.. SanitizationPolicy.TagsFor(profile).Order(StringComparer.Ordinal)]))
            .ToList());

    /// <remarks>
    /// The <c>Developer</c> profile is gated on the role that justifies it. It permits iframes and
    /// data attributes and is reachable only from the <c>html</c> field type, which is itself
    /// <c>DeveloperOnly</c> — so a preview that granted it to anyone who asked would be a way to
    /// have the server render markup the caller could not have authored.
    /// </remarks>
    private static IResult RenderAsync(
        MarkupPreviewRequest request,
        IMarkdownRenderer markdown,
        IContentSanitizer sanitizer,
        ICmsAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadProfile(request.Profile, out var profile))
        {
            return Invalid(
                FieldValidationCodes.RichTextProfile,
                $"'{request.Profile}' is not a sanitization profile.",
                nameof(request.Profile));
        }

        if (profile is SanitizationProfile.Developer &&
            !authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsProblems.Problem(
                HttpStatusCode.Forbidden,
                "forbidden",
                "Forbidden",
                ValidationResult.Error(
                    FieldValidationCodes.RichTextProfile,
                    "The developer profile is only available to the role that can author against it.",
                    nameof(request.Profile)));
        }

        var result = request.Format switch
        {
            MarkupFormats.Markdown => markdown.ToHtmlWithReport(request.Source, profile),
            MarkupFormats.Html => sanitizer.SanitizeWithReport(request.Source, profile),
            _ => null,
        };

        // Refused rather than guessed. Markdown rendered as HTML shows its source and HTML rendered
        // as markdown escapes its markup; neither degrades into something an author would recognise
        // as a preview, which is the same reason the field type refuses a value with no format.
        if (result is null)
        {
            return Invalid(
                FieldValidationCodes.RichTextFormat,
                $"'{request.Format}' is not a markup format; expected " +
                $"'{MarkupFormats.Markdown}' or '{MarkupFormats.Html}'.",
                nameof(request.Format));
        }

        return Results.Ok(new MarkupPreviewResult(result.Html, result.Removals));
    }

    /// <summary>Reads the requested profile, defaulting to the most restrictive one.</summary>
    /// <param name="requested">The profile name, or null.</param>
    /// <param name="profile">The profile to apply.</param>
    /// <returns><see langword="false"/> when a name was given and is not a profile.</returns>
    /// <remarks>
    /// Absent defaults to <see cref="SanitizationProfile.Basic"/>, matching what the renderer does
    /// for a property that configures none. A <em>mistyped</em> name is refused instead of falling
    /// back, because falling back would show an author a preview stripped harder than their zone
    /// will be and send them chasing a problem they do not have.
    /// </remarks>
    private static bool TryReadProfile(string? requested, out SanitizationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            profile = SanitizationProfile.Basic;

            return true;
        }

        return Enum.TryParse(requested, ignoreCase: true, out profile) &&
               Enum.IsDefined(profile);
    }

    private static IResult Invalid(string code, string message, string property) =>
        CmsProblems.Problem(
            HttpStatusCode.UnprocessableEntity,
            "validation",
            "Validation failed",
            ValidationResult.Error(code, message, property));
}
