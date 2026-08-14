using System.Text.Json.Serialization;

using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>PATCH /api/cms/v1/pages/{id}/metadata</c> — title, slug, SEO, and editorial metadata
/// (spec sections 14.7 and 18.1).
/// </summary>
/// <remarks>
/// Every member is a <see cref="Patch{T}"/>, so omitting one leaves the stored value alone and
/// sending it as <c>null</c> clears it. A body of <c>{"title": "Pricing"}</c> therefore changes the
/// title and nothing else, which is what a client that only knows about one field has every right to
/// expect.
/// <para>
/// The split between the two halves is where the value lives, and it is not cosmetic. Slug, owner,
/// review date, and navigation flag are properties of the <em>page</em> and take effect at once;
/// title and every SEO field belong to the <em>draft version</em>, so changing one no more disturbs
/// what is published than editing the payload does (spec section 11.1).
/// </para>
/// <para>
/// Nothing here can change a page's template, status, or position in the tree. Those are dedicated
/// operations with their own rules — a move rewrites descendant paths and emits redirects, a status
/// change goes through publishing — and accepting them as metadata would be the mass-assignment hole
/// spec section 20.1 names.
/// </para>
/// </remarks>
public sealed record PatchPageMetadataRequest
{
    /// <summary>Editor-facing title, stored on the draft version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string> Title { get; init; }

    /// <summary>The page's own URL segment. Re-checked against its siblings when supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string> Slug { get; init; }

    /// <summary>Whether the page's URL is stated outright rather than built from the tree.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<bool> UseExplicitUrl { get; init; }

    /// <summary>Full site-relative URL, required when <see cref="UseExplicitUrl"/> is set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> ExplicitUrl { get; init; }

    /// <summary>Whether generated navigation menus include the page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<bool> ShowInNavigation { get; init; }

    /// <summary>Editor accountable for keeping the page current, or null when nobody is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<int?> OwnerUserId { get; init; }

    /// <summary>Date the content is due a review, feeding the "needs review" dashboard.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<DateOnly?> ReviewByDate { get; init; }

    /// <summary>Note to other editors. Never rendered publicly.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> InternalNotes { get; init; }

    /// <summary>Overrides the <c>&lt;title&gt;</c> element. Falls back to the page title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> MetaTitle { get; init; }

    /// <summary>Meta description rendered into the page head.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> MetaDescription { get; init; }

    /// <summary>Explicit canonical URL, for pages reachable at more than one address.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> CanonicalUrl { get; init; }

    /// <summary>Whether search engines may index the page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<bool> RobotsIndex { get; init; }

    /// <summary>Whether search engines may follow links out of the page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<bool> RobotsFollow { get; init; }

    /// <summary>Open Graph title. Falls back to the meta title, then the page title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> OgTitle { get; init; }

    /// <summary>Open Graph description. Falls back to the meta description.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> OgDescription { get; init; }

    /// <summary>Open Graph object type, such as <c>article</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> OgType { get; init; }

    /// <summary>Twitter card type, such as <c>summary_large_image</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> TwitterCard { get; init; }

    /// <summary>Hand-authored JSON-LD emitted into the page head. Checked for well-formedness.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> StructuredDataJson { get; init; }

    /// <summary>Sitemap change-frequency hint, such as <c>weekly</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> ChangeFreq { get; init; }

    /// <summary>Sitemap priority between 0.0 and 1.0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<decimal?> Priority { get; init; }
}
