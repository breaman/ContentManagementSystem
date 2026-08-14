namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Stable diagnostic codes returned by the page management API.
/// </summary>
/// <remarks>
/// The page counterpart of <c>StructureCodes</c>, and separate from it for the reason that list gives
/// for its own existence: codes are the contract a client switches on, and a page refusal and a
/// template refusal need different remedies offered. A code does not change once shipped; the
/// wording beside it may be rewritten freely (spec section 22.2).
/// </remarks>
public static class PageCodes
{
    /// <summary>The page, version, or template addressed does not exist.</summary>
    public const string NotFound = "page.not-found";

    /// <summary>The caller is authenticated but holds no role permitting this.</summary>
    public const string Forbidden = "page.forbidden";

    /// <summary>A title was not supplied.</summary>
    /// <remarks>
    /// Required at creation because the slug is generated from it, and a page with no title has no
    /// address either (spec section 10.2).
    /// </remarks>
    public const string TitleRequired = "page.title-required";

    /// <summary>A supplied value is longer than the column that stores it.</summary>
    public const string TooLong = "page.too-long";

    /// <summary>
    /// No slug could be derived, because none was supplied and the title contains nothing usable.
    /// </summary>
    public const string SlugRequired = "page.slug-required";

    /// <summary>The slug is not a single usable URL segment.</summary>
    public const string SlugFormat = "page.slug-format";

    /// <summary>
    /// A root-level slug collides with a path the application already serves.
    /// </summary>
    /// <remarks>
    /// Only root-level slugs can collide: the reserved list in spec section 10.3 is a set of first
    /// segments, and <c>/products/admin</c> reaches no framework endpoint.
    /// </remarks>
    public const string SlugReserved = "page.slug-reserved";

    /// <summary>A sibling already uses this slug, so the two pages would share a URL.</summary>
    public const string SlugDuplicate = "page.slug-duplicate";

    /// <summary>
    /// The slug contains characters outside ASCII.
    /// </summary>
    /// <remarks>
    /// A warning, never an error: spec section 10.3 permits Unicode slugs and asks only that the
    /// homograph risk be shown to whoever typed one.
    /// </remarks>
    public const string SlugHomograph = "page.slug-homograph";

    /// <summary>A page opted out of the tree without saying what its URL is, or the reverse.</summary>
    public const string ExplicitUrlMismatch = "page.explicit-url-mismatch";

    /// <summary>The explicit URL is not a usable site-relative path.</summary>
    public const string ExplicitUrlFormat = "page.explicit-url-format";

    /// <summary>No template was named, or the one named does not exist.</summary>
    public const string TemplateNotFound = "page.template-not-found";

    /// <summary>
    /// The template exists but has been disabled, so no new page may be created from it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TemplateNotFound"/> because the remedy is different: a developer
    /// re-enables the template, rather than the editor picking another one.
    /// </remarks>
    public const string TemplateDisabled = "page.template-disabled";

    /// <summary>The requested parent page does not exist, or is in the recycle bin.</summary>
    public const string ParentNotFound = "page.parent-not-found";

    /// <summary>The page would sit deeper than the content tree allows.</summary>
    public const string TooDeep = "page.too-deep";

    /// <summary>
    /// The user named as the page's owner does not exist.
    /// </summary>
    /// <remarks>
    /// Checked rather than left to the foreign key, because a constraint violation reaches the
    /// client as a 500 about a database it is not supposed to know exists, and the ordinary way to
    /// reach this is picking someone whose account was removed while the editor had the form open.
    /// </remarks>
    public const string OwnerNotFound = "page.owner-not-found";

    /// <summary>A JSON-valued field was supplied but is not well-formed JSON.</summary>
    public const string MalformedJson = "page.malformed-json";

    /// <summary>A value falls outside the range the field permits.</summary>
    public const string OutOfRange = "page.out-of-range";

    /// <summary>
    /// Someone else saved this page between this request reading it and writing it.
    /// </summary>
    /// <remarks>
    /// Retryable: the client reloads the page and applies the change again. Reported rather than
    /// merged, because a silently lost edit is the failure optimistic concurrency exists to prevent
    /// (spec section 11.8).
    /// </remarks>
    public const string ConcurrentChange = "page.concurrent-change";

    /// <summary>The payload sent for a draft is not well-formed JSON.</summary>
    public const string MalformedPayload = "page.malformed-payload";

    /// <summary>
    /// The payload names a different template from the page it is being saved to.
    /// </summary>
    /// <remarks>
    /// A page's template is fixed at creation. Accepting a payload that names another one would let
    /// a client change the content model of a live page through the draft endpoint, which is the
    /// mass-assignment hole spec section 20.1 names, one level deeper.
    /// </remarks>
    public const string TemplateMismatch = "page.template-mismatch";

    /// <summary>
    /// The payload captures a template revision that is neither the draft's own nor the current one.
    /// </summary>
    /// <remarks>
    /// Moving forward to the template's current revision is how an editor adopts a structural change
    /// (spec section 8.5). Naming any other number is not an editorial act — it is a client that
    /// has invented a schema for its content to be judged against.
    /// </remarks>
    public const string TemplateRevisionInvalid = "page.template-revision-invalid";

    /// <summary>The content breaks a rule of the schema it was authored against.</summary>
    /// <remarks>
    /// A container for the field-type and payload-walk diagnostics rather than a diagnostic itself:
    /// the individual problems arrive with their own <c>field.*</c> and <c>payload.*</c> codes and
    /// their own paths, which is what lets the editor mark the offending zone.
    /// </remarks>
    public const string ContentInvalid = "page.content-invalid";

    /// <summary>The page has never been published, so there is nothing to reset the draft to.</summary>
    public const string NothingPublished = "page.nothing-published";

    /// <summary>The page is already unpublished.</summary>
    public const string AlreadyUnpublished = "page.already-unpublished";

    /// <summary>The version addressed does not belong to this page, or does not exist.</summary>
    public const string VersionNotFound = "page.version-not-found";

    /// <summary>The page is in the recycle bin, and the operation needs a live page.</summary>
    public const string PageDeleted = "page.deleted";

    /// <summary>The page is not in the recycle bin, so there is nothing to restore.</summary>
    public const string PageNotDeleted = "page.not-deleted";

    /// <summary>
    /// A permanent delete was refused because stored content still points at the page.
    /// </summary>
    /// <remarks>
    /// The rows are <c>ContentReference</c>, and the refusal names the pages holding them. Blocked
    /// outright rather than cascaded: the alternative is a published page whose link resolves to
    /// nothing, discovered by a visitor (spec section 14.10).
    /// </remarks>
    public const string StillReferenced = "page.still-referenced";

    /// <summary>
    /// A restored page's former parent is itself in the recycle bin.
    /// </summary>
    /// <remarks>
    /// A warning, not an error: the page comes back at the site root rather than being unrestorable,
    /// which is what spec section 14.10 asks for.
    /// </remarks>
    public const string ParentStillDeleted = "page.parent-still-deleted";
}
