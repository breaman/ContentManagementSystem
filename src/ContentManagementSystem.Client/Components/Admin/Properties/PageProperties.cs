using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Client.Components.Admin.Properties;

/// <summary>
/// What the properties panel edits, and how it turns that back into a patch (task P6-17).
/// </summary>
/// <remarks>
/// A mutable model beside the immutable <see cref="PageDetail"/> the panel was handed, because a
/// record cannot be two-way bound and because the panel has to be able to answer "what did this
/// editor actually change" — which is the whole point of <see cref="Patch{T}"/>.
/// <para>
/// <strong>Only changed members are sent.</strong> A panel that patched all twenty fields on every
/// save would overwrite whatever a colleague changed in the meantime with values this editor never
/// touched, turning a partial update into the mass assignment the patch contract exists to avoid —
/// and it would do it silently, because the values look right on the screen that sent them.
/// </para>
/// </remarks>
public sealed class PageProperties
{
    /// <summary>
    /// Editor-facing title, stored on the draft version.
    /// </summary>
    /// <remarks>
    /// Carries no <c>[Required]</c>, deliberately. Nothing renders this model through an
    /// <c>EditForm</c>, so an annotation here would be decoration that never runs; the rule lives on
    /// the server, and a refusal comes back naming this member and lands on the box.
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    /// <summary>The page's own URL segment.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Whether the URL is stated outright rather than built from the tree.</summary>
    public bool UseExplicitUrl { get; set; }

    /// <summary>Full site-relative URL, used only when <see cref="UseExplicitUrl"/> is set.</summary>
    public string? ExplicitUrl { get; set; }

    /// <summary>Whether generated navigation menus include the page.</summary>
    public bool ShowInNavigation { get; set; } = true;

    /// <summary>Editor accountable for keeping the page current.</summary>
    public int? OwnerUserId { get; set; }

    /// <summary>Date the content is due a review.</summary>
    public DateOnly? ReviewByDate { get; set; }

    /// <summary>Note to other editors. Never rendered publicly.</summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// The labels this page carries (task P8-20, spec section 14.7).
    /// </summary>
    /// <remarks>
    /// A list rather than a comma-separated string, because a tag may contain a comma and the panel
    /// should not be the place that decides it may not. The chips are the editing surface; this is
    /// what they add to and remove from.
    /// </remarks>
    public List<string> Tags { get; set; } = [];

    /// <summary>Overrides the page's <c>&lt;title&gt;</c> element.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Meta description rendered into the page head.</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Explicit canonical URL.</summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>Whether search engines may index the page.</summary>
    public bool RobotsIndex { get; set; } = true;

    /// <summary>Whether search engines may follow links out of the page.</summary>
    public bool RobotsFollow { get; set; } = true;

    /// <summary>Open Graph title.</summary>
    public string? OgTitle { get; set; }

    /// <summary>Open Graph description.</summary>
    public string? OgDescription { get; set; }

    /// <summary>Media item used as the Open Graph image.</summary>
    public int? OgImageMediaId { get; set; }

    /// <summary>Open Graph object type.</summary>
    public string? OgType { get; set; }

    /// <summary>Twitter card type.</summary>
    public string? TwitterCard { get; set; }

    /// <summary>Hand-authored JSON-LD emitted into the page head.</summary>
    public string? StructuredDataJson { get; set; }

    /// <summary>Sitemap change-frequency hint.</summary>
    public string? ChangeFreq { get; set; }

    /// <summary>Sitemap priority between 0.0 and 1.0.</summary>
    public decimal? Priority { get; set; }

    /// <summary>Reads a page into an editable model.</summary>
    /// <param name="page">The page as the server last reported it.</param>
    /// <returns>The model the panel binds to.</returns>
    public static PageProperties From(PageDetail page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PageProperties
        {
            Title = page.Summary.Title,
            Slug = page.Summary.Slug,
            UseExplicitUrl = page.UseExplicitUrl,
            ExplicitUrl = page.ExplicitUrl,
            ShowInNavigation = page.Summary.ShowInNavigation,
            OwnerUserId = page.OwnerUserId,
            ReviewByDate = page.ReviewByDate,
            InternalNotes = page.InternalNotes,
            Tags = [.. page.Tags ?? []],
            MetaTitle = page.Seo.MetaTitle,
            MetaDescription = page.Seo.MetaDescription,
            CanonicalUrl = page.Seo.CanonicalUrl,
            RobotsIndex = page.Seo.RobotsIndex,
            RobotsFollow = page.Seo.RobotsFollow,
            OgTitle = page.Seo.OgTitle,
            OgDescription = page.Seo.OgDescription,
            OgImageMediaId = page.Seo.OgImageMediaId,
            OgType = page.Seo.OgType,
            TwitterCard = page.Seo.TwitterCard,
            StructuredDataJson = page.Seo.StructuredDataJson,
            ChangeFreq = page.Seo.ChangeFreq,
            Priority = page.Seo.Priority,
        };
    }

    /// <summary>Whether anything at all differs from what the server holds.</summary>
    /// <param name="page">The page as the server last reported it.</param>
    /// <returns><see langword="true"/> when there is something to save.</returns>
    public bool HasChanges(PageDetail page) => Changes(page).Count > 0;

    /// <summary>
    /// Builds the patch for exactly the members this editor changed.
    /// </summary>
    /// <param name="page">The page as the server last reported it.</param>
    /// <returns>The request body, carrying nothing that did not move.</returns>
    public PatchPageMetadataRequest ToPatch(PageDetail page)
    {
        var changed = Changes(page);

        return new PatchPageMetadataRequest
        {
            Title = Set<string>(changed, nameof(Title), Title.Trim()),
            Slug = Set<string>(changed, nameof(Slug), Slug.Trim()),
            UseExplicitUrl = Set<bool>(changed, nameof(UseExplicitUrl), UseExplicitUrl),
            ExplicitUrl = Set<string?>(changed, nameof(ExplicitUrl), Clean(ExplicitUrl)),
            ShowInNavigation = Set<bool>(changed, nameof(ShowInNavigation), ShowInNavigation),
            OwnerUserId = Set<int?>(changed, nameof(OwnerUserId), OwnerUserId),
            ReviewByDate = Set<DateOnly?>(changed, nameof(ReviewByDate), ReviewByDate),
            InternalNotes = Set<string?>(changed, nameof(InternalNotes), Clean(InternalNotes)),
            Tags = Set<IReadOnlyList<string>>(changed, nameof(Tags), [.. Tags]),
            MetaTitle = Set<string?>(changed, nameof(MetaTitle), Clean(MetaTitle)),
            MetaDescription = Set<string?>(changed, nameof(MetaDescription), Clean(MetaDescription)),
            CanonicalUrl = Set<string?>(changed, nameof(CanonicalUrl), Clean(CanonicalUrl)),
            RobotsIndex = Set<bool>(changed, nameof(RobotsIndex), RobotsIndex),
            RobotsFollow = Set<bool>(changed, nameof(RobotsFollow), RobotsFollow),
            OgTitle = Set<string?>(changed, nameof(OgTitle), Clean(OgTitle)),
            OgDescription = Set<string?>(changed, nameof(OgDescription), Clean(OgDescription)),
            OgType = Set<string?>(changed, nameof(OgType), Clean(OgType)),
            TwitterCard = Set<string?>(changed, nameof(TwitterCard), Clean(TwitterCard)),
            StructuredDataJson = Set<string?>(changed, nameof(StructuredDataJson), Clean(StructuredDataJson)),
            ChangeFreq = Set<string?>(changed, nameof(ChangeFreq), Clean(ChangeFreq)),
            Priority = Set<decimal?>(changed, nameof(Priority), Priority),
        };
    }

    /// <summary>
    /// The names of the members that differ from what the server holds.
    /// </summary>
    /// <remarks>
    /// Compared on the cleaned value, not on the raw box contents, so that clearing a field down to
    /// spaces is the same edit as clearing it — otherwise a panel would keep reporting a change it
    /// then sends as no change at all, and the "unsaved" indicator would never go out.
    /// <para>
    /// <c>OgImageMediaId</c> is absent on purpose: the patch contract carries no member for it, so
    /// there is nothing this panel could send. See the note on the panel itself.
    /// </para>
    /// </remarks>
    private HashSet<string> Changes(PageDetail page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var changed = new HashSet<string>(StringComparer.Ordinal);

        void Compare<T>(string name, T mine, T theirs)
        {
            if (!EqualityComparer<T>.Default.Equals(mine, theirs)) changed.Add(name);
        }

        Compare(nameof(Title), Title.Trim(), page.Summary.Title);
        Compare(nameof(Slug), Slug.Trim(), page.Summary.Slug);
        Compare(nameof(UseExplicitUrl), UseExplicitUrl, page.UseExplicitUrl);
        Compare(nameof(ExplicitUrl), Clean(ExplicitUrl), Clean(page.ExplicitUrl));
        Compare(nameof(ShowInNavigation), ShowInNavigation, page.Summary.ShowInNavigation);
        Compare(nameof(OwnerUserId), OwnerUserId, page.OwnerUserId);
        Compare(nameof(ReviewByDate), ReviewByDate, page.ReviewByDate);
        Compare(nameof(InternalNotes), Clean(InternalNotes), Clean(page.InternalNotes));

        // Order is not a change: the server returns them alphabetically and the panel appends to the
        // end, so comparing the sequences would report an edit every time a tag was added and
        // reloaded.
        if (!Tags.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                (page.Tags ?? []).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            changed.Add(nameof(Tags));
        }
        Compare(nameof(MetaTitle), Clean(MetaTitle), Clean(page.Seo.MetaTitle));
        Compare(nameof(MetaDescription), Clean(MetaDescription), Clean(page.Seo.MetaDescription));
        Compare(nameof(CanonicalUrl), Clean(CanonicalUrl), Clean(page.Seo.CanonicalUrl));
        Compare(nameof(RobotsIndex), RobotsIndex, page.Seo.RobotsIndex);
        Compare(nameof(RobotsFollow), RobotsFollow, page.Seo.RobotsFollow);
        Compare(nameof(OgTitle), Clean(OgTitle), Clean(page.Seo.OgTitle));
        Compare(nameof(OgDescription), Clean(OgDescription), Clean(page.Seo.OgDescription));
        Compare(nameof(OgType), Clean(OgType), Clean(page.Seo.OgType));
        Compare(nameof(TwitterCard), Clean(TwitterCard), Clean(page.Seo.TwitterCard));
        Compare(nameof(StructuredDataJson), Clean(StructuredDataJson), Clean(page.Seo.StructuredDataJson));
        Compare(nameof(ChangeFreq), Clean(ChangeFreq), Clean(page.Seo.ChangeFreq));
        Compare(nameof(Priority), Priority, page.Seo.Priority);

        return changed;
    }

    /// <summary>Supplies a member only when it changed, leaving it absent otherwise.</summary>
    private static Patch<T> Set<T>(HashSet<string> changed, string name, T? value) =>
        changed.Contains(name) ? new Patch<T>(value) : default;

    /// <summary>Treats whitespace as absent, so a cleared box stores as null rather than as "  ".</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
