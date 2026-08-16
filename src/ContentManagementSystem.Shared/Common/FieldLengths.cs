namespace ContentManagementSystem.Shared.Common;

/// <summary>
/// Canonical maximum storage lengths shared by EF Core configurations and API contracts.
/// Defined once here so a validation attribute and a column definition can never drift apart
/// (plan section 4.7).
/// </summary>
public static class FieldLengths
{
    /// <summary>Person and student first, middle, and last names.</summary>
    public const int PersonName = 100;

    /// <summary>Group, item, and promotion names.</summary>
    public const int EntityName = 200;

    /// <summary>Email addresses; matches the maximum practical RFC 5321 path length.</summary>
    public const int Email = 320;

    /// <summary>Phone numbers as entered, including punctuation.</summary>
    public const int Phone = 32;

    /// <summary>A single street address line.</summary>
    public const int AddressLine = 200;

    /// <summary>City or state/region name.</summary>
    public const int CityOrRegion = 100;

    /// <summary>Postal code.</summary>
    public const int PostalCode = 20;

    /// <summary>Country name.</summary>
    public const int Country = 100;

    /// <summary>Item and promotion descriptions. Plain text only in this release.</summary>
    public const int Description = 4000;

    /// <summary>Barcode prefix as configured on an item.</summary>
    public const int BarcodePrefix = 50;

    /// <summary>Generated inventory-unit barcode.</summary>
    public const int Barcode = 80;

    /// <summary>Promotion code, stored invariant-uppercase.</summary>
    public const int PromotionCode = 50;

    /// <summary>Free-text reason recorded against a status change or adjustment.</summary>
    public const int Reason = 500;

    /// <summary>Staff notification message body.</summary>
    public const int NotificationMessage = 1000;

    /// <summary>Payment method descriptor.</summary>
    public const int PaymentMethod = 100;

    /// <summary>External payment reference (never full card data).</summary>
    public const int ExternalReference = 200;

    /// <summary>Idempotency keys supplied by callers for replay-safe mutations.</summary>
    public const int IdempotencyKey = 128;

    /// <summary>Outbox/notification event type discriminator.</summary>
    public const int EventType = 200;

    // ---------------------------------------------------------------------------------------
    // Content management system (spec section 23). Names of CMS columns reuse EntityName, which
    // already carries the 200 the spec asks for; a second 200-length "Name" constant would only
    // create two ways to say the same thing.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Stable content-model keys: template, zone, block type, composition, and field type keys.
    /// </summary>
    /// <remarks>
    /// These appear inside stored content payloads, so the length is part of the storage contract
    /// rather than a display preference. Keys are immutable once content references them.
    /// </remarks>
    public const int ContentKey = 100;

    /// <summary>
    /// Absolute or site-relative URLs, including page routes and redirect targets.
    /// </summary>
    /// <remarks>
    /// 2000 is the practical ceiling browsers and proxies agree on. Note that a column this wide
    /// exceeds SQL Server's 900-byte index key limit — index the <c>binary(32)</c> hash companion
    /// column instead of the URL itself.
    /// </remarks>
    public const int Url = 2000;

    /// <summary>Editor-facing help text on a zone, block type, template, or composition.</summary>
    /// <remarks>
    /// Distinct from <see cref="Description"/>, which is the much longer catalogue description
    /// inherited from the commerce domain.
    /// </remarks>
    public const int ShortDescription = 500;

    /// <summary>SEO meta description rendered into the page head.</summary>
    public const int MetaDescription = 500;

    /// <summary>Assembly-qualified name of the Razor component backing a template or block type.</summary>
    public const int ComponentTypeName = 500;

    /// <summary>Icon identifier shown against a block type in the block picker.</summary>
    public const int IconKey = 50;

    /// <summary>Token pattern producing a one-line summary of a collapsed block in the editor.</summary>
    public const int SummaryTemplate = 500;

    /// <summary>Tab or accordion grouping label applied to a zone or block-type property.</summary>
    public const int GroupName = 100;

    /// <summary>BCP-47 culture name, such as <c>en-US</c>.</summary>
    public const int Culture = 16;

    /// <summary>IANA or Windows time zone identifier.</summary>
    public const int TimeZoneId = 100;

    /// <summary>Free-text note recorded against a structural revision.</summary>
    public const int RevisionNotes = 1000;

    /// <summary>Site verification token supplied by a search engine.</summary>
    public const int VerificationToken = 200;

    /// <summary>A single URL path segment, as authored on a page.</summary>
    /// <remarks>
    /// The per-segment ceiling from spec section 10.3. Distinct from <see cref="ContentKey"/>
    /// despite sharing a number: a slug is editor-facing text that changes freely, while a content
    /// key is immutable once a payload quotes it.
    /// </remarks>
    public const int Slug = 100;

    /// <summary>
    /// Materialized ancestor path on a tree node, such as <c>/1/8/44/</c>.
    /// </summary>
    /// <remarks>
    /// 800 rather than the spec's 900 so the column can carry an index key. A nonclustered index
    /// key is limited to 1700 bytes and silently includes the clustering key, which
    /// <c>nvarchar(900)</c> — 1800 bytes on its own — exceeds; SQL Server would create the index
    /// with a warning and then fail inserts of long values. <c>PageTreeService.MaxDepth</c> bounds
    /// the real worst case far below this.
    /// </remarks>
    public const int MaterializedPath = 800;

    /// <summary>Title of a page or content item, as shown in the browser tab and search results.</summary>
    public const int ContentTitle = 500;

    /// <summary>Editorial note attached to a page for other editors, never rendered publicly.</summary>
    public const int InternalNotes = 2000;

    /// <summary>Open Graph type and Twitter card discriminators, such as <c>article</c>.</summary>
    public const int SocialCardType = 50;

    /// <summary>Sitemap change-frequency hint, such as <c>weekly</c>.</summary>
    public const int ChangeFrequency = 20;

    /// <summary>File name of an uploaded media item, server-generated and as originally sent.</summary>
    /// <remarks>
    /// 255 is what every filesystem this may be stored on agrees to. The server-generated name is far
    /// shorter; the length is here for the client-supplied original, which is displayed but never
    /// used to address anything (spec section 23.3).
    /// </remarks>
    public const int FileName = 255;

    /// <summary>An IANA media type, such as <c>image/jpeg</c>.</summary>
    public const int MimeType = 100;

    /// <summary>Key an object is stored under in the media store.</summary>
    /// <remarks>
    /// Wide enough for a container prefix plus a content-addressed name. Keys are server-generated
    /// from content hashes and never contain a client-supplied path (spec section 13.2).
    /// </remarks>
    public const int StorageKey = 500;

    /// <summary>Caption rendered alongside a media item.</summary>
    public const int Caption = 1000;

    /// <summary>Normalized rendition spec — size, mode, format, quality, and geometry.</summary>
    public const int RenditionSpec = 500;

    /// <summary>Short image format token, such as <c>webp</c>.</summary>
    public const int ImageFormat = 10;
}