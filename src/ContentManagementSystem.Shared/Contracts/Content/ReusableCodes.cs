namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Stable diagnostic codes returned by the reusable-content management API (spec section 9).
/// </summary>
/// <remarks>
/// A vocabulary of its own, for the reason <see cref="PageCodes"/> gives for not sharing the
/// structure one: a code is what a client switches on to decide which remedy to offer, and the
/// remedies here are different. "This item is still on forty pages" is answered by opening the
/// where-used panel, not by renaming anything.
/// <para>
/// Codes do not change once shipped; the wording beside them may be rewritten freely
/// (spec section 22.2).
/// </para>
/// </remarks>
public static class ReusableCodes
{
    /// <summary>The reusable item or version addressed does not exist.</summary>
    public const string NotFound = "reusable.not-found";

    /// <summary>The caller is authenticated but holds no role permitting this.</summary>
    public const string Forbidden = "reusable.forbidden";

    /// <summary>A stable key was not supplied, or is not of the permitted shape.</summary>
    /// <remarks>
    /// The shape rules are <c>ContentKeys</c>' — the same ones templates and block types obey,
    /// because a reusable item's key is the same kind of thing: an identifier quoted by imports and
    /// deployment scripts and never changed afterwards.
    /// </remarks>
    public const string KeyInvalid = "reusable.key-invalid";

    /// <summary>Another item already holds that key.</summary>
    /// <remarks>
    /// Including one in the recycle bin. The unique index is deliberately unfiltered: a deleted item
    /// still owns its key, so that restoring it cannot fail on a constraint the editor who took the
    /// key never saw.
    /// </remarks>
    public const string KeyDuplicate = "reusable.key-duplicate";

    /// <summary>A key change was refused because stored content already addresses the item.</summary>
    public const string KeyImmutable = "reusable.key-immutable";

    /// <summary>A display name was not supplied.</summary>
    public const string NameRequired = "reusable.name-required";

    /// <summary>A supplied value is longer than the column that stores it.</summary>
    public const string TooLong = "reusable.too-long";

    /// <summary>No block type was named, or the one named does not exist.</summary>
    public const string BlockTypeNotFound = "reusable.block-type-not-found";

    /// <summary>The payload sent for a draft is not well-formed JSON.</summary>
    public const string MalformedPayload = "reusable.malformed-payload";

    /// <summary>
    /// The payload names a different block type from the item it is being saved to.
    /// </summary>
    /// <remarks>
    /// An item's shape is fixed at creation. Accepting a payload naming another block type would let
    /// a client change the content model of a live item through the draft endpoint — the
    /// mass-assignment hole of spec section 20.1, one level deeper.
    /// </remarks>
    public const string BlockTypeMismatch = "reusable.block-type-mismatch";

    /// <summary>
    /// The payload captures a block type revision that is neither the draft's own nor the current one.
    /// </summary>
    public const string BlockTypeRevisionInvalid = "reusable.block-type-revision-invalid";

    /// <summary>The content breaks a rule of the schema it was authored against.</summary>
    public const string ContentInvalid = "reusable.content-invalid";

    /// <summary>Someone else saved this item between this request reading it and writing it.</summary>
    public const string ConcurrentChange = "reusable.concurrent-change";

    /// <summary>The item has never been published, so there is nothing to reset the draft to.</summary>
    public const string NothingPublished = "reusable.nothing-published";

    /// <summary>The item is already unpublished.</summary>
    public const string AlreadyUnpublished = "reusable.already-unpublished";

    /// <summary>
    /// A delete was refused because stored content still places this item (spec section 9.4).
    /// </summary>
    /// <remarks>
    /// Blocked outright rather than cascaded, and blocked at the <em>soft</em> delete rather than
    /// only at the purge: a deleted item is invisible to the resolver, so cascading it would blank a
    /// zone on every page that placed it, discovered by a visitor. The refusal carries the
    /// where-used list, because "replace the references first" is not actionable without it.
    /// </remarks>
    public const string StillReferenced = "reusable.still-referenced";

    /// <summary>
    /// The item's content places the item itself, directly or through another item.
    /// </summary>
    /// <remarks>
    /// Refused at write time, which is the only place it can be refused usefully: at render time
    /// all that is left is a depth guard, and a guard that fires is a page that renders half a
    /// footer (acceptance criterion P4 #7).
    /// </remarks>
    public const string Cycle = "reusable.cycle";

    /// <summary>
    /// A publish or unpublish will change pages that were not themselves republished.
    /// </summary>
    /// <remarks>
    /// A warning, and the one the confirmation dialog of spec section 9.4 is built on. It is not an
    /// error because changing every page at once is the entire point of reusable content — what the
    /// warning buys is that nobody does it by accident.
    /// </remarks>
    public const string BlastRadius = "reusable.large-blast-radius";
}
