using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A link to a page, an external site, a media item, an anchor on the current page, or an email
/// address (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "link", "kind": "page", "pageId": 44, "text": "Get started",
/// "target": "_self", "rel": null }</c>, with the member carrying the destination decided by
/// <c>kind</c>.
/// <para>
/// <strong>An internal link stores a <c>pageId</c> and never a URL string</strong> (decision D6).
/// This is the whole reason moving or renaming a page is a safe operation: the URL is resolved at
/// render time from the page's current route, so a link authored today still points at the right
/// place after the target moves twice. A stored URL would be a copy that nothing updates.
/// </para>
/// <para>
/// Configuration key: the P3 addition <c>allowedKinds</c>.
/// </para>
/// <para>
/// <strong>Completed in P3</strong> with the routing layer: the picker, resolution of a
/// <c>pageId</c> to its current URL, the draft-link badging inside preview, and the check that the
/// target page exists. Reference extraction ships now rather than then — see
/// <see cref="MediaFieldType"/> for why a reference-bearing field type cannot defer that half.
/// </para>
/// </remarks>
public sealed class LinkFieldType : FieldTypeBase
{
    /// <summary>A link to another page in this site, stored by identity.</summary>
    public const string PageKind = "page";

    /// <summary>A link to a URL on another site.</summary>
    public const string ExternalKind = "external";

    /// <summary>A link to a file in the media library, stored by identity.</summary>
    public const string MediaKind = "media";

    /// <summary>A jump to a fragment on the page the link is rendered on.</summary>
    public const string AnchorKind = "anchor";

    /// <summary>A <c>mailto:</c> link.</summary>
    public const string EmailKind = "email";

    /// <summary>The member deciding which of the destination members applies.</summary>
    public const string KindMember = "kind";

    private const string PageIdMember = "pageId";
    private const string UrlMember = "url";
    private const string AnchorMember = "anchor";
    private const string EmailMember = "email";
    private const string TextMember = "text";
    private const string TargetMember = "target";

    /// <summary>The browsing contexts a link may name. Anything else is an author's typo.</summary>
    private static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Link;

    /// <inheritdoc />
    public override string DisplayName => "Link";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities =>
        FieldTypeCapabilities.ReferenceBearing | FieldTypeCapabilities.Searchable;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.TextList(
                "allowedKinds",
                "The link kinds an editor may pick. An empty list allows all of them.",
                allowedValues: [PageKind, ExternalKind, MediaKind, AnchorKind, EmailKind],
                notEnforcedUntil: "P3"),
        ]);


    /// <inheritdoc />
    protected override string PayloadMember => KindMember;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        List<ValidationDiagnostic>? diagnostics = null;

        var kind = value.ValueKind is JsonValueKind.String ? value.GetString() : null;

        switch (kind)
        {
            case PageKind:
                RequireId(property, PageIdMember, "a page", ref diagnostics);
                break;

            case MediaKind:
                RequireId(property, MediaValue.MediaIdMember, "a media item", ref diagnostics);
                break;

            case ExternalKind:
                ValidateExternalUrl(property, ref diagnostics);
                break;

            case AnchorKind:
                RequireText(property, AnchorMember, FieldValidationCodes.LinkAnchor,
                    "An anchor link needs the fragment to jump to.", ref diagnostics);
                break;

            case EmailKind:
                ValidateEmail(property, ref diagnostics);
                break;

            default:
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.LinkKind,
                    $"A link must be one of '{PageKind}', '{ExternalKind}', '{MediaKind}', " +
                    $"'{AnchorKind}', or '{EmailKind}'.",
                    KindMember);
                break;
        }

        ValidateText(property, ref diagnostics);
        ValidateTarget(property, ref diagnostics);

        return Result(diagnostics);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the two internal kinds point at anything this system owns. An external URL, an anchor,
    /// and an email address depend on nothing that can change underneath them, so they produce no
    /// row and no invalidation.
    /// </remarks>
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        var kind = GetStringValue(value);

        if (kind == PageKind && StoredId.TryRead(value, PageIdMember, out var pageId))
        {
            yield return new ContentReference(ContentReferenceTargetType.Page, pageId);
        }
        else if (kind == MediaKind && StoredId.TryRead(value, MediaValue.MediaIdMember, out var mediaId))
        {
            yield return new ContentReference(ContentReferenceTargetType.Media, mediaId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The visible label, which is authored content and reads as part of the page. The destination
    /// is not indexed: matching a page because of a URL it links to would return the wrong page for
    /// the query.
    /// </remarks>
    public override string ExtractSearchText(JsonElement value) =>
        SearchText.Collapse(ReadString(value, TextMember));

    private static void RequireId(
        JsonElement property,
        string member,
        string description,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!StoredId.TryRead(property, member, out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.ReferenceId,
                $"This link does not identify {description}.",
                member);
        }
    }

    private static void ValidateExternalUrl(
        JsonElement property,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var url = ReadString(property, UrlMember);

        // The scheme allowlist is the security-relevant rule here: without it a link picker is a
        // route to storing "javascript:…" as a page's call to action. The renderer applies the same
        // allowlist on the way out (ADR 0008), because rows written before this check existed are
        // not covered by it.
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            !IsWebScheme(parsed.Scheme))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.LinkUrl,
                "An external link must be an absolute http or https URL.",
                UrlMember);
        }
    }

    /// <summary>Whether a parsed scheme is one a browser will navigate to safely.</summary>
    /// <param name="scheme">The scheme, already lowercased by <see cref="Uri"/>.</param>
    /// <returns><see langword="true"/> for <c>http</c> and <c>https</c> only.</returns>
    private static bool IsWebScheme(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

    private static void ValidateEmail(JsonElement property, ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!IsMailbox(ReadString(property, EmailMember)))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.LinkEmail,
                "An email link needs an address with a mailbox and a domain.",
                EmailMember);
        }
    }

    /// <summary>
    /// Whether an address is worth putting in a <c>mailto:</c>.
    /// </summary>
    /// <param name="email">The stored address.</param>
    /// <returns><see langword="true"/> when it could be a mailbox.</returns>
    /// <remarks>
    /// Deliberately the weakest check that still catches a mistake. An address is only really
    /// validated by delivering to it, and the stricter patterns in circulation are all known for
    /// refusing addresses that work.
    /// </remarks>
    private static bool IsMailbox(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.AsSpan().ContainsAny(" \t\r\n")) return false;

        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
    }

    private static void RequireText(
        JsonElement property,
        string member,
        string code,
        string message,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(ReadString(property, member)))
        {
            Diagnostics.AddError(ref diagnostics, code, message, member);
        }
    }

    private static void ValidateText(JsonElement property, ref List<ValidationDiagnostic>? diagnostics)
    {
        if (GetMember(property, TextMember) is { ValueKind: not (JsonValueKind.Undefined or
            JsonValueKind.Null or JsonValueKind.String) })
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.Shape,
                "Link text must be text.",
                TextMember);
        }
    }

    private static void ValidateTarget(JsonElement property, ref List<ValidationDiagnostic>? diagnostics)
    {
        var target = GetMember(property, TargetMember);

        if (target.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;

        if (target.ValueKind is not JsonValueKind.String ||
            Array.IndexOf(Targets, target.GetString()) < 0)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.LinkTarget,
                $"A link target must be one of {string.Join(", ", Targets)}.",
                TargetMember);
        }
    }

    private static string? ReadString(JsonElement property, string member) =>
        GetMember(property, member) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
